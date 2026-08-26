namespace GaltekClassroom.Agent.Session.Lifecycle;

public sealed class ReconnectBackoff
{
    private readonly TimeSpan[] _delays;
    private int _attempt;

    public ReconnectBackoff(IReadOnlyList<TimeSpan> delays)
    {
        if (delays.Count == 0)
        {
            throw new ArgumentException("At least one reconnect delay is required.", nameof(delays));
        }

        if (delays.Any(delay => delay < TimeSpan.Zero))
        {
            throw new ArgumentException("Reconnect delays cannot be negative.", nameof(delays));
        }

        _delays = delays.ToArray();
    }

    public TimeSpan NextDelay()
    {
        var delay = _delays[Math.Min(_attempt, _delays.Length - 1)];
        _attempt++;
        return delay;
    }

    public void Reset()
    {
        _attempt = 0;
    }
}
