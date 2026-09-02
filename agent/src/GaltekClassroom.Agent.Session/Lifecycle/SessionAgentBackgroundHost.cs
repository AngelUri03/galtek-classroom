using GaltekClassroom.Agent.Session.Commands;

namespace GaltekClassroom.Agent.Session.Lifecycle;

public sealed class SessionAgentBackgroundHost
{
    private readonly ISessionInstanceLock _instanceLock;
    private readonly ISessionContext _sessionContext;
    private readonly SessionAgentSupervisor _supervisor;
    private readonly ISessionCommandServer _commandServer;

    public SessionAgentBackgroundHost(
        ISessionInstanceLock instanceLock,
        ISessionContext sessionContext,
        SessionAgentSupervisor supervisor,
        ISessionCommandServer? commandServer = null)
    {
        _instanceLock = instanceLock;
        _sessionContext = sessionContext;
        _supervisor = supervisor;
        _commandServer = commandServer ?? new NoOpSessionCommandServer();
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var lockHandle = _instanceLock.TryAcquire();
        if (!lockHandle.Acquired)
        {
            return 0;
        }

        if (_sessionContext.SessionId == 0)
        {
            return 1;
        }

        await Task.WhenAll(
            _supervisor.RunAsync(cancellationToken),
            _commandServer.RunAsync(_sessionContext.SessionId, cancellationToken));
        return 0;
    }
}
