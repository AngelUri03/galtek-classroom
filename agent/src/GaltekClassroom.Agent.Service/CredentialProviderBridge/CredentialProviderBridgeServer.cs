using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.CredentialProviderBridge;

public sealed record CredentialProviderBridgeServerOptions(
    string PipeName,
    int MaxConcurrentConnections)
{
    public static CredentialProviderBridgeServerOptions Default { get; } = new(
        CredentialProviderBridgeProtocol.PipeName,
        CredentialProviderBridgeProtocol.MaxConcurrentConnections);
}

public interface ICredentialProviderBridgePipeStreamFactory
{
    NamedPipeServerStream CreateServerStream(CredentialProviderBridgeServerOptions options);
}

public sealed class UnsupportedCredentialProviderBridgePipeStreamFactory : ICredentialProviderBridgePipeStreamFactory
{
    public NamedPipeServerStream CreateServerStream(CredentialProviderBridgeServerOptions options)
    {
        throw new PlatformNotSupportedException("Galtek Classroom Credential Provider bridge uses Windows Named Pipes.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class CredentialProviderBridgePipeStreamFactory : ICredentialProviderBridgePipeStreamFactory
{
    public NamedPipeServerStream CreateServerStream(CredentialProviderBridgeServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return NamedPipeServerStreamAcl.Create(
            options.PipeName,
            PipeDirection.InOut,
            options.MaxConcurrentConnections,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: CreatePipeSecurity());
    }

    public static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize,
            AccessControlType.Allow));

        return security;
    }
}

public sealed class CredentialProviderBridgeServer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CredentialProviderBridgeServerOptions _options;
    private readonly ICredentialProviderBridgePipeStreamFactory _pipeStreamFactory;
    private readonly ICredentialProviderCallerVerifier _callerVerifier;
    private readonly CredentialProviderBridgeRequestHandler _requestHandler;
    private readonly ILogger<CredentialProviderBridgeServer> _logger;

    public CredentialProviderBridgeServer(
        CredentialProviderBridgeServerOptions options,
        ICredentialProviderBridgePipeStreamFactory pipeStreamFactory,
        ICredentialProviderCallerVerifier callerVerifier,
        CredentialProviderBridgeRequestHandler requestHandler,
        ILogger<CredentialProviderBridgeServer> logger)
    {
        _options = options;
        _pipeStreamFactory = pipeStreamFactory;
        _callerVerifier = callerVerifier;
        _requestHandler = requestHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Credential Provider bridge server starting on pipe {PipeName}.",
            _options.PipeName);

        var acceptLoops = new Task[_options.MaxConcurrentConnections];
        for (var index = 0; index < acceptLoops.Length; index++)
        {
            acceptLoops[index] = AcceptLoopAsync(stoppingToken);
        }

        await Task.WhenAll(acceptLoops).ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = _pipeStreamFactory.CreateServerStream(_options);

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogDebug("CP_BRIDGE_ACCEPTED.");
                await ProcessConnectionAsync(pipe, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Credential Provider bridge client disconnected unexpectedly.");
            }
            catch (CredentialProviderBridgeFramingException exception)
            {
                _logger.LogWarning(exception, "Credential Provider bridge malformed frame rejected.");
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Credential Provider bridge malformed JSON rejected.");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Credential Provider bridge connection failed.");
            }
        }
    }

    private async Task ProcessConnectionAsync(
        PipeStream pipe,
        CancellationToken cancellationToken)
    {
        // ImpersonateNamedPipeClient uses the security context of the last message read from the pipe.
        var requestJson = await CredentialProviderBridgeFraming.ReadJsonAsync(pipe, cancellationToken)
            .ConfigureAwait(false);

        var caller = _callerVerifier.Verify(pipe);
        if (!caller.Authorized)
        {
            LogCallerValidationFailure(caller);

            try
            {
                await WriteResponseAsync(
                    pipe,
                    CredentialProviderBridgeResponse.Error(
                        string.Empty,
                        CredentialProviderBridgeErrorCodes.Unauthorized),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                _logger.LogDebug(
                    exception,
                    "CP_BRIDGE_REJECTION_RESPONSE_PEER_CLOSED stage={FailureStage} clientPid={ClientProcessId}.",
                    caller.FailureStage ?? CredentialProviderCallerValidationStages.Unknown,
                    caller.ClientProcessId);
            }

            return;
        }

        _logger.LogDebug(
            "CALLER_VALIDATION_PASSED clientPid={ClientProcessId}.",
            caller.ClientProcessId);

        _requestHandler.MarkListenerObserved();
        _logger.LogDebug(
            "FRESH_PRESENCE_MARKED clientPid={ClientProcessId}.",
            caller.ClientProcessId);

        var operation = TryReadOperation(requestJson);
        _logger.LogDebug(
            "CP_BRIDGE_REQUEST operation={Operation}.",
            operation);

        using var response = await _requestHandler.HandleFrameAsync(requestJson, caller, cancellationToken)
            .ConfigureAwait(false);
        if (response.BinaryPayload is not null)
        {
            _logger.LogDebug(
                "CP_BRIDGE_RESPONSE operation={Operation} payload=binary.",
                operation);
            await CredentialProviderBridgeFraming.WritePayloadAsync(
                pipe,
                response.BinaryPayload,
                cancellationToken).ConfigureAwait(false);
            _logger.LogDebug(
                "CP_BRIDGE_WRITE_COMPLETED operation={Operation} payload=binary.",
                operation);
            return;
        }

        _logger.LogDebug(
            "CP_BRIDGE_RESPONSE operation={Operation} status={Status} errorCode={ErrorCode}.",
            operation,
            response.JsonResponse!.Status,
            response.JsonResponse.ErrorCode);
        await WriteResponseAsync(pipe, response.JsonResponse!, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug(
            "CP_BRIDGE_WRITE_COMPLETED operation={Operation} status={Status}.",
            operation,
            response.JsonResponse.Status);
    }

    private static async Task WriteResponseAsync(
        Stream pipe,
        CredentialProviderBridgeResponse response,
        CancellationToken cancellationToken)
    {
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await CredentialProviderBridgeFraming.WriteJsonAsync(pipe, responseJson, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string TryReadOperation(string requestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(requestJson);
            if (document.RootElement.TryGetProperty("operation", out var operation)
                && operation.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(operation.GetString()))
            {
                return operation.GetString()!;
            }
        }
        catch (JsonException)
        {
        }

        return "UNKNOWN";
    }

    private void LogCallerValidationFailure(CredentialProviderCallerValidation caller)
    {
        var stage = string.IsNullOrWhiteSpace(caller.FailureStage)
            ? CredentialProviderCallerValidationStages.Unknown
            : caller.FailureStage;
        if (caller.WindowsErrorCode is int windowsErrorCode)
        {
            _logger.LogWarning(
                "CALLER_VALIDATION_FAILED:{FailureStage} clientPid={ClientProcessId} win32={Win32ErrorCode}.",
                stage,
                caller.ClientProcessId,
                windowsErrorCode);
            return;
        }

        _logger.LogWarning(
            "CALLER_VALIDATION_FAILED:{FailureStage} clientPid={ClientProcessId}.",
            stage,
            caller.ClientProcessId);
    }
}

public sealed class NoOpCredentialProviderBridgeServer : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
