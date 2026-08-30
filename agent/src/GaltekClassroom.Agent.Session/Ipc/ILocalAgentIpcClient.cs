using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Ipc;

public interface ILocalAgentIpcClient
{
    Task<LocalAgentIpcResult<LocalIpcPingPayload>> TryPingAsync(CancellationToken cancellationToken);

    Task<LocalIpcPingPayload> PingAsync(CancellationToken cancellationToken);

    Task<LocalAgentIpcResult<LocalDeviceStatus>> TryGetDeviceStatusAsync(CancellationToken cancellationToken);

    Task<LocalDeviceStatus> GetDeviceStatusAsync(CancellationToken cancellationToken);
}

public sealed record LocalAgentIpcResult<TPayload>(
    bool Succeeded,
    TPayload? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static LocalAgentIpcResult<TPayload> Success(TPayload payload)
    {
        return new LocalAgentIpcResult<TPayload>(true, payload, null, null);
    }

    public static LocalAgentIpcResult<TPayload> Failure(string errorCode, string errorMessage)
    {
        return new LocalAgentIpcResult<TPayload>(false, default, errorCode, errorMessage);
    }
}
