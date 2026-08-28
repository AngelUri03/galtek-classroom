using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.Pairing;
using GaltekClassroom.Agent.Service.Runtime;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Grpc.Core;
using Grpc.Net.Client;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class MasterGrpcConnectionClient
{
    private readonly ILogger<MasterGrpcConnectionClient> _logger;
    private readonly MasterConnectionOptions _options;
    private readonly TrustedMasterResolver _trustedMasterResolver;
    private readonly MasterCertificatePinningPolicy _certificatePinningPolicy;
    private readonly ClientHelloFactory _helloFactory;
    private readonly INetworkIdentityKeyStore _keyStore;
    private readonly ISystemClock _clock;
    private readonly MasterConnectionStateTracker _stateTracker;
    private readonly RemoteOperationDispatcher _operationDispatcher;

    public MasterGrpcConnectionClient(
        ILogger<MasterGrpcConnectionClient> logger,
        MasterConnectionOptions options,
        TrustedMasterResolver trustedMasterResolver,
        MasterCertificatePinningPolicy certificatePinningPolicy,
        ClientHelloFactory helloFactory,
        INetworkIdentityKeyStore keyStore,
        ISystemClock clock,
        MasterConnectionStateTracker stateTracker,
        RemoteOperationDispatcher operationDispatcher)
    {
        _logger = logger;
        _options = options;
        _trustedMasterResolver = trustedMasterResolver;
        _certificatePinningPolicy = certificatePinningPolicy;
        _helloFactory = helloFactory;
        _keyStore = keyStore;
        _clock = clock;
        _stateTracker = stateTracker;
        _operationDispatcher = operationDispatcher;
    }

    public async Task RunAsync(
        InstallationIdentity installationIdentity,
        NetworkIdentityMetadata clientNetworkIdentity,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(installationIdentity);
        ArgumentNullException.ThrowIfNull(clientNetworkIdentity);

        if (!_options.Enabled)
        {
            _logger.LogInformation("Master gRPC connection is disabled.");
            return;
        }

        var backoff = new MasterConnectionBackoff(_options.ReconnectDelays);
        var failureStreak = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var masterNetworkIdentityId = _options.MasterNetworkIdentityId;
            try
            {
                await ConnectUntilDisconnectedAsync(
                    installationIdentity,
                    clientNetworkIdentity,
                    stoppingToken);
                backoff.Reset();
                failureStreak = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is InvalidOperationException or RpcException or IOException)
            {
                if (failureStreak == 0)
                {
                    _logger.LogWarning(
                        exception,
                        "Secure Master gRPC connection failed. Retrying with backoff.");
                }
                else
                {
                    _logger.LogDebug(
                        exception,
                        "Secure Master gRPC connection is still failing. Retrying with backoff.");
                }

                failureStreak++;
                _stateTracker.SetOffline(
                    masterNetworkIdentityId,
                    _clock.UtcNow,
                    exception is RpcException rpcException ? rpcException.StatusCode.ToString() : null,
                    exception.Message);
            }

            var delay = backoff.NextDelay();
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ConnectUntilDisconnectedAsync(
        InstallationIdentity installationIdentity,
        NetworkIdentityMetadata clientNetworkIdentity,
        CancellationToken stoppingToken)
    {
        if (installationIdentity.InstallationId != clientNetworkIdentity.InstallationId)
        {
            throw new InvalidOperationException("Client Network Identity does not match Installation Identity.");
        }

        if (string.IsNullOrWhiteSpace(_options.MasterEndpoint)
            || !Uri.TryCreate(_options.MasterEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("MasterEndpoint must be an absolute HTTPS URI.");
        }

        if (_options.MasterNetworkIdentityId is not { } masterNetworkIdentityId)
        {
            throw new InvalidOperationException("MasterNetworkIdentityId is required for secure transport.");
        }

        _stateTracker.SetConnecting(masterNetworkIdentityId, _clock.UtcNow);

        var trustedMaster = await _trustedMasterResolver.ResolveAsync(
            masterNetworkIdentityId,
            clientNetworkIdentity,
            stoppingToken);
        if (!trustedMaster.Trusted)
        {
            throw new InvalidOperationException(
                $"{trustedMaster.ErrorCode ?? MasterConnectionConstants.MutualTlsRequiredErrorCode}: "
                + trustedMaster.ErrorMessage);
        }

        var certificate = _keyStore.CreateSelfSignedCertificate(
            clientNetworkIdentity.KeyName,
            $"Galtek Client {clientNetworkIdentity.NetworkIdentityId:D}",
            _clock.UtcNow.AddMinutes(-5),
            _clock.UtcNow.Add(_options.CertificateLifetime));
        if (!certificate.Created || certificate.Certificate is null)
        {
            throw new InvalidOperationException(
                certificate.ErrorMessage ?? "Client mTLS certificate could not be created.");
        }

        var hello = _helloFactory.Create(clientNetworkIdentity, _options);
        if (!hello.Created || hello.Hello is null)
        {
            throw new InvalidOperationException($"{hello.ErrorCode}: {hello.ErrorMessage}");
        }

        using var clientCertificate = certificate.Certificate;
        using var handler = CreateHttpHandler(clientCertificate, trustedMaster.Master!);
        using var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            HttpHandler = handler,
            MaxReceiveMessageSize = _options.MaxMessageBytes,
            MaxSendMessageSize = _options.MaxMessageBytes
        });

        var client = new NetworkConnection.NetworkConnectionClient(channel);
        using var call = client.Connect(cancellationToken: stoppingToken);
        using var writeLock = new SemaphoreSlim(1, 1);
        await WriteAsync(call.RequestStream, new ClientEnvelope
        {
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            ClientHello = hello.Hello
        }, writeLock, stoppingToken);

        var readTask = ReadResponsesAsync(
            call.ResponseStream,
            call.RequestStream,
            writeLock,
            masterNetworkIdentityId,
            clientNetworkIdentity,
            trustedMaster.Master!,
            stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (readTask.IsCompleted)
            {
                await readTask;
                throw new IOException("Master gRPC stream ended.");
            }

            await Task.Delay(_options.HeartbeatInterval, stoppingToken);
            // Heartbeat stays disk-idle; operation requests re-check trust before any action.
            await WriteAsync(call.RequestStream, new ClientEnvelope
            {
                ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
                Heartbeat = new Heartbeat
                {
                    HeartbeatId = Guid.NewGuid().ToString("N"),
                    SentAtUnixMs = _clock.UtcNow.ToUnixTimeMilliseconds()
                }
            }, writeLock, stoppingToken);
        }
    }

    private async Task ReadResponsesAsync(
        IAsyncStreamReader<MasterEnvelope> responseStream,
        IClientStreamWriter<ClientEnvelope> requestStream,
        SemaphoreSlim writeLock,
        Guid masterNetworkIdentityId,
        NetworkIdentityMetadata clientNetworkIdentity,
        AuthorizedMasterTrustRecord trustedMaster,
        CancellationToken cancellationToken)
    {
        while (await responseStream.MoveNext(cancellationToken))
        {
            var response = responseStream.Current;
            if (!string.Equals(
                response.ProtocolVersion,
                MasterConnectionConstants.ProtocolVersion,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Master returned an unsupported network protocol version.");
            }

            switch (response.PayloadCase)
            {
                case MasterEnvelope.PayloadOneofCase.ConnectionStatus:
                    HandleConnectionStatus(masterNetworkIdentityId, response.ConnectionStatus);
                    break;
                case MasterEnvelope.PayloadOneofCase.HeartbeatAck:
                    _stateTracker.SetOnline(masterNetworkIdentityId, _clock.UtcNow);
                    break;
                case MasterEnvelope.PayloadOneofCase.OperationRequest:
                    await HandleOperationRequestAsync(
                        requestStream,
                        writeLock,
                        masterNetworkIdentityId,
                        clientNetworkIdentity,
                        trustedMaster,
                        response.OperationRequest,
                        cancellationToken);
                    break;
            }
        }
    }

    private async Task HandleOperationRequestAsync(
        IClientStreamWriter<ClientEnvelope> requestStream,
        SemaphoreSlim writeLock,
        Guid masterNetworkIdentityId,
        NetworkIdentityMetadata clientNetworkIdentity,
        AuthorizedMasterTrustRecord trustedMaster,
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureTrustedMasterStillCurrentAsync(
            masterNetworkIdentityId,
            clientNetworkIdentity,
            trustedMaster,
            cancellationToken);

        RemoteOperationDispatchResult dispatch =
            await _operationDispatcher.DispatchAsync(request, cancellationToken).ConfigureAwait(false);

        await WriteAsync(requestStream, new ClientEnvelope
        {
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            OperationAccepted = dispatch.Accepted
        }, writeLock, cancellationToken).ConfigureAwait(false);

        await WriteAsync(requestStream, new ClientEnvelope
        {
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            OperationResult = dispatch.Result
        }, writeLock, cancellationToken).ConfigureAwait(false);
    }

    private void HandleConnectionStatus(Guid masterNetworkIdentityId, ConnectionStatus status)
    {
        switch (status.Status)
        {
            case ConnectionState.Online:
                _stateTracker.SetOnline(masterNetworkIdentityId, _clock.UtcNow);
                break;
            case ConnectionState.Connecting:
                _stateTracker.SetConnecting(masterNetworkIdentityId, _clock.UtcNow);
                break;
            case ConnectionState.Rejected:
                _stateTracker.SetOffline(
                    masterNetworkIdentityId,
                    _clock.UtcNow,
                    status.ReasonCode,
                    status.Message);
                throw new InvalidOperationException($"{status.ReasonCode}: {status.Message}");
            default:
                _stateTracker.SetOffline(
                    masterNetworkIdentityId,
                    _clock.UtcNow,
                    status.ReasonCode,
                    status.Message);
                break;
        }
    }

    private SocketsHttpHandler CreateHttpHandler(
        X509Certificate2 clientCertificate,
        AuthorizedMasterTrustRecord trustedMaster)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            ConnectTimeout = _options.ConnectTimeout
        };

        handler.SslOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificates = new X509CertificateCollection { clientCertificate },
            RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            {
                X509Certificate2? certificate2 = null;
                try
                {
                    certificate2 = certificate switch
                    {
                        null => null,
                        X509Certificate2 existing => existing,
                        _ => new X509Certificate2(certificate)
                    };

                    return _certificatePinningPolicy.IsCertificateTrusted(
                        certificate2,
                        trustedMaster,
                        _clock.UtcNow);
                }
                finally
                {
                    if (certificate2 is not null && !ReferenceEquals(certificate2, certificate))
                    {
                        certificate2.Dispose();
                    }
                }
            }
        };

        return handler;
    }

    private async Task EnsureTrustedMasterStillCurrentAsync(
        Guid masterNetworkIdentityId,
        NetworkIdentityMetadata clientNetworkIdentity,
        AuthorizedMasterTrustRecord trustedMaster,
        CancellationToken cancellationToken)
    {
        var currentTrust = await _trustedMasterResolver.ResolveAsync(
            masterNetworkIdentityId,
            clientNetworkIdentity,
            cancellationToken);
        if (!currentTrust.Trusted
            || currentTrust.Master is null
            || !string.Equals(
                currentTrust.Master.MasterPublicKeyFingerprint,
                trustedMaster.MasterPublicKeyFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                currentTrust.Master.MasterPublicKeySubjectPublicKeyInfoBase64,
                trustedMaster.MasterPublicKeySubjectPublicKeyInfoBase64,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{currentTrust.ErrorCode ?? MasterConnectionConstants.MutualTlsRequiredErrorCode}: "
                + currentTrust.ErrorMessage);
        }
    }

    private static async Task WriteAsync(
        IClientStreamWriter<ClientEnvelope> requestStream,
        ClientEnvelope envelope,
        SemaphoreSlim writeLock,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await requestStream.WriteAsync(envelope).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }
}
