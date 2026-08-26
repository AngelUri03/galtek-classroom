namespace GaltekClassroom.Agent.Session.Lifecycle;

public sealed class SessionAgentBackgroundHost
{
    private readonly ISessionInstanceLock _instanceLock;
    private readonly ISessionContext _sessionContext;
    private readonly SessionAgentSupervisor _supervisor;

    public SessionAgentBackgroundHost(
        ISessionInstanceLock instanceLock,
        ISessionContext sessionContext,
        SessionAgentSupervisor supervisor)
    {
        _instanceLock = instanceLock;
        _sessionContext = sessionContext;
        _supervisor = supervisor;
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

        await _supervisor.RunAsync(cancellationToken);
        return 0;
    }
}
