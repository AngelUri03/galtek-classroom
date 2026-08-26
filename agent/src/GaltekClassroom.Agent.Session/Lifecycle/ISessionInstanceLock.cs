namespace GaltekClassroom.Agent.Session.Lifecycle;

public interface ISessionInstanceLock
{
    SessionInstanceLockHandle TryAcquire();
}

public sealed class SessionInstanceLockHandle : IDisposable
{
    private readonly Mutex? _mutex;
    private bool _disposed;

    public SessionInstanceLockHandle(bool acquired, Mutex? mutex)
    {
        Acquired = acquired;
        _mutex = mutex;
    }

    public bool Acquired { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_mutex is not null)
        {
            _mutex.Dispose();
        }

        _disposed = true;
    }
}
