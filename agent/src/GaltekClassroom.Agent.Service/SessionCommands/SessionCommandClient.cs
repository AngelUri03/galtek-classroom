using System.IO.Pipes;
using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.SessionCommands;

public sealed record SessionCommandClientOptions(TimeSpan Timeout)
{
    public static SessionCommandClientOptions Default { get; } = new(TimeSpan.FromSeconds(2));
}

public sealed record SessionCommandClientResult(
    bool Succeeded,
    string? ErrorCode,
    string? Message)
{
    public static SessionCommandClientResult Success()
    {
        return new SessionCommandClientResult(true, null, null);
    }

    public static SessionCommandClientResult Failure(string errorCode, string? message = null)
    {
        return new SessionCommandClientResult(false, errorCode, message);
    }
}

public sealed class SessionCommandClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IInteractiveSessionResolver _sessionResolver;
    private readonly ISessionAgentServerVerifier _serverVerifier;
    private readonly SessionCommandClientOptions _options;

    public SessionCommandClient(
        IInteractiveSessionResolver sessionResolver,
        ISessionAgentServerVerifier serverVerifier,
        SessionCommandClientOptions options)
    {
        _sessionResolver = sessionResolver;
        _serverVerifier = serverVerifier;
        _options = options;
    }

    public Task<SessionCommandClientResult> PingAsync(CancellationToken cancellationToken)
    {
        return SendAsync(
            new SessionCommandRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                CommandType = SessionCommandTypes.ChannelPing
            },
            cancellationToken);
    }

    public async Task<SessionCommandClientResult> SendAsync(
        SessionCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = _sessionResolver.Resolve();
        if (!session.Available || session.SessionId <= 0)
        {
            return SessionCommandClientResult.Failure(
                session.ErrorCode ?? SessionCommandErrorCodes.SessionAgentUnavailable);
        }

        var pipeName = SessionCommandProtocol.PipeNameForSession(session.SessionId);
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            await pipe.ConnectAsync((int)_options.Timeout.TotalMilliseconds, timeout.Token)
                .ConfigureAwait(false);

            if (!_serverVerifier.Verify(pipe, session.SessionId))
            {
                return SessionCommandClientResult.Failure(
                    SessionCommandErrorCodes.SessionChannelUnauthorized);
            }

            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            await SessionCommandFraming.WriteJsonAsync(pipe, requestJson, timeout.Token)
                .ConfigureAwait(false);

            var responseJson = await SessionCommandFraming.ReadJsonAsync(pipe, timeout.Token)
                .ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<SessionCommandResponse>(responseJson, JsonOptions);

            return ValidateResponse(request, response);
        }
        catch (TimeoutException)
        {
            return SessionCommandClientResult.Failure(
                SessionCommandErrorCodes.SessionChannelTimeout);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SessionCommandClientResult.Failure(
                SessionCommandErrorCodes.SessionChannelTimeout);
        }
        catch (UnauthorizedAccessException)
        {
            return SessionCommandClientResult.Failure(
                SessionCommandErrorCodes.SessionChannelUnauthorized);
        }
        catch (IOException)
        {
            return SessionCommandClientResult.Failure(
                SessionCommandErrorCodes.SessionAgentUnavailable);
        }
        catch (JsonException)
        {
            return SessionCommandClientResult.Failure(
                SessionCommandErrorCodes.SessionChannelInvalidResponse);
        }
        catch (SessionCommandFramingException)
        {
            return SessionCommandClientResult.Failure(
                SessionCommandErrorCodes.SessionChannelInvalidResponse);
        }
    }

    private static SessionCommandClientResult ValidateResponse(
        SessionCommandRequest request,
        SessionCommandResponse? response)
    {
        if (response is null)
        {
            return SessionCommandClientResult.Failure(
                SessionCommandErrorCodes.SessionChannelInvalidResponse);
        }

        if (response.ProtocolVersion != SessionCommandProtocol.ProtocolVersion)
        {
            return SessionCommandClientResult.Failure(
                SessionCommandErrorCodes.SessionChannelProtocolMismatch);
        }

        if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal))
        {
            return SessionCommandClientResult.Failure(
                SessionCommandErrorCodes.SessionChannelInvalidResponse);
        }

        if (string.Equals(response.Status, SessionCommandStatuses.Success, StringComparison.Ordinal))
        {
            return SessionCommandClientResult.Success();
        }

        return SessionCommandClientResult.Failure(
            response.ErrorCode ?? SessionCommandErrorCodes.SessionChannelInvalidResponse,
            response.Message);
    }
}
