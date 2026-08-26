using System.Diagnostics;

namespace GaltekClassroom.Agent.Session.Lifecycle;

public interface ISessionContext
{
    int ProcessId { get; }

    int SessionId { get; }

    string UserName { get; }
}

public sealed record SessionContext(
    int ProcessId,
    int SessionId,
    string UserName) : ISessionContext;

public sealed class WindowsSessionContext : ISessionContext
{
    public int ProcessId => Environment.ProcessId;

    public int SessionId => Process.GetCurrentProcess().SessionId;

    public string UserName => Environment.UserName;
}
