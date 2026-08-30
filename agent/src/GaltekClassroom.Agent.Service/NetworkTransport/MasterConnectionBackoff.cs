namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class MasterConnectionBackoff
{
    private readonly IReadOnlyList<TimeSpan> _delays;
    private readonly IReconnectJitter _jitter;
    private readonly TimeSpan _retryJitterMax;
    private int _attempt;

    public MasterConnectionBackoff(IReadOnlyList<TimeSpan> delays)
        : this(delays, new FixedReconnectJitter(TimeSpan.Zero), TimeSpan.Zero)
    {
    }

    public MasterConnectionBackoff(
        IReadOnlyList<TimeSpan> delays,
        IReconnectJitter jitter,
        TimeSpan retryJitterMax)
    {
        if (delays is null || delays.Count == 0 || delays.Any(delay => delay <= TimeSpan.Zero))
        {
            throw new ArgumentException("Reconnect delays must contain at least one positive value.", nameof(delays));
        }

        ArgumentNullException.ThrowIfNull(jitter);
        if (retryJitterMax < TimeSpan.Zero)
        {
            throw new ArgumentException("Reconnect jitter cannot be negative.", nameof(retryJitterMax));
        }

        _delays = delays;
        _jitter = jitter;
        _retryJitterMax = retryJitterMax;
    }

    public TimeSpan InitialDelay(TimeSpan initialJitterMax)
    {
        if (initialJitterMax < TimeSpan.Zero)
        {
            throw new ArgumentException("Initial jitter cannot be negative.", nameof(initialJitterMax));
        }

        return _jitter.NextJitter(initialJitterMax);
    }

    public TimeSpan NextDelay()
    {
        var index = Math.Min(_attempt, _delays.Count - 1);
        _attempt++;

        return _delays[index] + _jitter.NextJitter(_retryJitterMax);
    }

    public void Reset()
    {
        _attempt = 0;
    }

    private sealed class FixedReconnectJitter : IReconnectJitter
    {
        private readonly TimeSpan _value;

        public FixedReconnectJitter(TimeSpan value)
        {
            _value = value;
        }

        public TimeSpan NextJitter(TimeSpan maxJitter)
        {
            return maxJitter <= TimeSpan.Zero ? TimeSpan.Zero : _value;
        }
    }
}
