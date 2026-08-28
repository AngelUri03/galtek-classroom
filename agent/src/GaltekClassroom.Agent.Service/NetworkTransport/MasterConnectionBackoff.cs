namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class MasterConnectionBackoff
{
    private readonly IReadOnlyList<TimeSpan> _delays;
    private int _attempt;

    public MasterConnectionBackoff(IReadOnlyList<TimeSpan> delays)
    {
        if (delays is null || delays.Count == 0 || delays.Any(delay => delay <= TimeSpan.Zero))
        {
            throw new ArgumentException("Reconnect delays must contain at least one positive value.", nameof(delays));
        }

        _delays = delays;
    }

    public TimeSpan NextDelay()
    {
        var index = Math.Min(_attempt, _delays.Count - 1);
        _attempt++;

        return _delays[index];
    }

    public void Reset()
    {
        _attempt = 0;
    }
}
