using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Lifecycle;

public sealed class NamedMutexSessionInstanceLock : ISessionInstanceLock
{
    private readonly string _mutexName;

    public NamedMutexSessionInstanceLock()
        : this(ProductInfo.SessionAgentMutexName)
    {
    }

    public NamedMutexSessionInstanceLock(string mutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("Mutex name is required.", nameof(mutexName));
        }

        _mutexName = mutexName;
    }

    public SessionInstanceLockHandle TryAcquire()
    {
        // The named mutex is used as a per-session kernel marker; ownership would be thread-affine in async code.
        var mutex = new Mutex(initiallyOwned: false, _mutexName, out var createdNew);

        if (!createdNew)
        {
            mutex.Dispose();
            return new SessionInstanceLockHandle(acquired: false, mutex: null);
        }

        return new SessionInstanceLockHandle(acquired: true, mutex);
    }
}
