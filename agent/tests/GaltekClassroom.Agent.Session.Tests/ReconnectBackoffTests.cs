using GaltekClassroom.Agent.Session.Lifecycle;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class ReconnectBackoffTests
{
    [Fact]
    public void NextDelay_ReturnsCappedSequence()
    {
        var backoff = new ReconnectBackoff(
        [
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        ]);

        var delays = Enumerable.Range(0, 6)
            .Select(_ => backoff.NextDelay())
            .ToArray();

        Assert.Equal(
        [
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30)
        ], delays);
    }

    [Fact]
    public void Reset_StartsSequenceAgain()
    {
        var backoff = new ReconnectBackoff(
        [
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5)
        ]);

        _ = backoff.NextDelay();
        _ = backoff.NextDelay();
        backoff.Reset();

        Assert.Equal(TimeSpan.FromSeconds(2), backoff.NextDelay());
    }
}
