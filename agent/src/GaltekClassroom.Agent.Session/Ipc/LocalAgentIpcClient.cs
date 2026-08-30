using System.IO.Pipes;
using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Ipc;

public sealed class LocalAgentIpcException : Exception
{
    public LocalAgentIpcException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public sealed class LocalAgentIpcClient : ILocalAgentIpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
    private static readonly Dictionary<string, object?> EmptyPayload = new(capacity: 0, StringComparer.Ordinal);

    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    public LocalAgentIpcClient()
        : this(LocalIpcProtocol.PipeName, TimeSpan.FromSeconds(2))
    {
    }

    public LocalAgentIpcClient(string pipeName, TimeSpan connectTimeout)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name is required.", nameof(pipeName));
        }

        _pipeName = pipeName;
        _connectTimeout = connectTimeout;
    }

    public Task<LocalIpcPingPayload> PingAsync(CancellationToken cancellationToken)
    {
        return SendAsync<LocalIpcPingPayload>(LocalIpcOperations.Ping, cancellationToken);
    }

    public Task<LocalAgentIpcResult<LocalIpcPingPayload>> TryPingAsync(CancellationToken cancellationToken)
    {
        return TrySendAsync<LocalIpcPingPayload>(LocalIpcOperations.Ping, cancellationToken);
    }

    public Task<LocalDeviceStatus> GetDeviceStatusAsync(CancellationToken cancellationToken)
    {
        return SendAsync<LocalDeviceStatus>(LocalIpcOperations.GetDeviceStatus, cancellationToken);
    }

    public Task<LocalAgentIpcResult<LocalDeviceStatus>> TryGetDeviceStatusAsync(CancellationToken cancellationToken)
    {
        return TrySendAsync<LocalDeviceStatus>(LocalIpcOperations.GetDeviceStatus, cancellationToken);
    }

    private async Task<TPayload> SendAsync<TPayload>(
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await TrySendAsync<TPayload>(operation, cancellationToken);
        if (result.Succeeded && result.Payload is not null)
        {
            return result.Payload;
        }

        throw new LocalAgentIpcException(
            result.ErrorCode ?? LocalIpcErrorCodes.InternalError,
            result.ErrorMessage ?? $"IPC operation failed: {result.ErrorCode ?? LocalIpcErrorCodes.InternalError}.");
    }

    private async Task<LocalAgentIpcResult<TPayload>> TrySendAsync<TPayload>(
        string operation,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("D");
        var request = new SessionLocalIpcRequest
        {
            RequestId = requestId,
            Operation = operation
        };

        await using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, cancellationToken);
            await LocalIpcFraming.WriteJsonAsync(
                pipe,
                JsonSerializer.Serialize(request, JsonOptions),
                cancellationToken);

            var responseJson = await LocalIpcFraming.ReadJsonAsync(pipe, cancellationToken);
            var response = JsonSerializer.Deserialize<LocalIpcResponse>(responseJson, JsonOptions);
            if (response is null)
            {
                return LocalAgentIpcResult<TPayload>.Failure(
                    LocalIpcErrorCodes.MalformedRequest,
                    "IPC response was empty.");
            }

            if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
            {
                return LocalAgentIpcResult<TPayload>.Failure(
                    LocalIpcErrorCodes.ResponseMismatch,
                    "IPC response requestId did not match the request.");
            }

            if (!response.Success)
            {
                var errorCode = response.ErrorCode ?? LocalIpcErrorCodes.InternalError;
                return LocalAgentIpcResult<TPayload>.Failure(
                    errorCode,
                    $"IPC operation failed: {errorCode}.");
            }

            if (response.Payload is not JsonElement payload)
            {
                return LocalAgentIpcResult<TPayload>.Failure(
                    LocalIpcErrorCodes.MalformedRequest,
                    "IPC response payload was missing.");
            }

            var typedPayload = payload.Deserialize<TPayload>(JsonOptions);
            if (typedPayload is null)
            {
                return LocalAgentIpcResult<TPayload>.Failure(
                    LocalIpcErrorCodes.MalformedRequest,
                    "IPC response payload could not be deserialized.");
            }

            return LocalAgentIpcResult<TPayload>.Success(typedPayload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException exception)
        {
            return LocalAgentIpcResult<TPayload>.Failure(
                "LOCAL_AGENT_UNAVAILABLE",
                $"Local Agent Service did not accept the IPC connection: {exception.Message}");
        }
        catch (IOException exception)
        {
            return LocalAgentIpcResult<TPayload>.Failure(
                "LOCAL_AGENT_UNAVAILABLE",
                $"Local Agent Service is unavailable: {exception.Message}");
        }
        catch (LocalIpcFramingException exception)
        {
            return LocalAgentIpcResult<TPayload>.Failure(
                LocalIpcErrorCodes.MalformedRequest,
                $"IPC response frame was invalid: {exception.Message}");
        }
        catch (JsonException exception)
        {
            return LocalAgentIpcResult<TPayload>.Failure(
                LocalIpcErrorCodes.MalformedRequest,
                $"IPC response JSON was invalid: {exception.Message}");
        }
    }

    private sealed record SessionLocalIpcRequest
    {
        public int ProtocolVersion { get; init; } = LocalIpcProtocol.ProtocolVersion;

        public string RequestId { get; init; } = string.Empty;

        public string Operation { get; init; } = string.Empty;

        public object Payload { get; init; } = EmptyPayload;
    }
}
