using System.Security.Cryptography;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public interface IReconnectJitter
{
    TimeSpan NextJitter(TimeSpan maxJitter);
}

public sealed class RandomReconnectJitter : IReconnectJitter
{
    public TimeSpan NextJitter(TimeSpan maxJitter)
    {
        if (maxJitter <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var maxMilliseconds = (int)Math.Min(maxJitter.TotalMilliseconds, int.MaxValue - 1);
        if (maxMilliseconds <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(0, maxMilliseconds + 1));
    }
}
