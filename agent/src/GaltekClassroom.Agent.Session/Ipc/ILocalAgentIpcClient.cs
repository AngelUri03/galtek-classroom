using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Ipc;

public interface ILocalAgentIpcClient
{
    Task<LocalIpcPingPayload> PingAsync(CancellationToken cancellationToken);

    Task<LocalDeviceStatus> GetDeviceStatusAsync(CancellationToken cancellationToken);
}
