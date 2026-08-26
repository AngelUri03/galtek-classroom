namespace GaltekClassroom.Agent.Service.Ipc;

public interface ILocalIpcRequestHandler
{
    Task<string> HandleAsync(
        string requestJson,
        LocalIpcClientContext clientContext,
        CancellationToken cancellationToken);
}
