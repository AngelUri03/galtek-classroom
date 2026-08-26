using GaltekClassroom.Agent.Session.Lifecycle;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class SessionInstanceLockTests
{
    [Fact]
    public void TryAcquire_WhenSameSessionAlreadyHasInstance_RejectsSecondInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var mutexName = $@"Local\GaltekClassroom.Agent.Session.Tests.{Guid.NewGuid():N}";
        var firstLock = new NamedMutexSessionInstanceLock(mutexName);
        var secondLock = new NamedMutexSessionInstanceLock(mutexName);

        using var first = firstLock.TryAcquire();
        using var second = secondLock.TryAcquire();

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
    }
}
