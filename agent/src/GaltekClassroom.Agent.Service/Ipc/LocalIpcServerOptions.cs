using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Ipc;

public sealed record LocalIpcServerOptions(
    string PipeName,
    int MaxConcurrentConnections)
{
    public static LocalIpcServerOptions Default { get; } = new(
        LocalIpcProtocol.PipeName,
        LocalIpcProtocol.MaxConcurrentConnections);
}
