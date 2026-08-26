namespace GaltekClassroom.Agent.Session.Lifecycle;

public interface ISessionAgentDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemSessionAgentDelay : ISessionAgentDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
