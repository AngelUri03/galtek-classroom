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

public sealed class LocalAgentIpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

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

    public Task<LocalDeviceStatus> GetDeviceStatusAsync(CancellationToken cancellationToken)
    {
        return SendAsync<LocalDeviceStatus>(LocalIpcOperations.GetDeviceStatus, cancellationToken);
    }

    private async Task<TPayload> SendAsync<TPayload>(
        string operation,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("D");
        var request = new LocalIpcRequest
        {
            RequestId = requestId,
            Operation = operation,
            Payload = new Dictionary<string, object?>()
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
            var response = JsonSerializer.Deserialize<LocalIpcResponse>(responseJson, JsonOptions)
                ?? throw new LocalAgentIpcException(
                    LocalIpcErrorCodes.MalformedRequest,
                    "IPC response was empty.");

            if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
            {
                throw new LocalAgentIpcException(
                    LocalIpcErrorCodes.ResponseMismatch,
                    "IPC response requestId did not match the request.");
            }

            if (!response.Success)
            {
                throw new LocalAgentIpcException(
                    response.ErrorCode ?? LocalIpcErrorCodes.InternalError,
                    $"IPC operation failed: {response.ErrorCode ?? LocalIpcErrorCodes.InternalError}.");
            }

            if (response.Payload is not JsonElement payload)
            {
                throw new LocalAgentIpcException(
                    LocalIpcErrorCodes.MalformedRequest,
                    "IPC response payload was missing.");
            }

            return payload.Deserialize<TPayload>(JsonOptions)
                ?? throw new LocalAgentIpcException(
                    LocalIpcErrorCodes.MalformedRequest,
                    "IPC response payload could not be deserialized.");
        }
        catch (TimeoutException exception)
        {
            throw new LocalAgentIpcException(
                "LOCAL_AGENT_UNAVAILABLE",
                $"Local Agent Service did not accept the IPC connection: {exception.Message}");
        }
        catch (IOException exception)
        {
            throw new LocalAgentIpcException(
                "LOCAL_AGENT_UNAVAILABLE",
                $"Local Agent Service is unavailable: {exception.Message}");
        }
    }
}
