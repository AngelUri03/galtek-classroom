namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class RemoteOperationOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan LogonManagedAccountTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan DedupeRetention { get; init; } = TimeSpan.FromMinutes(30);

    public int MaxTrackedOperationIds { get; init; } = 1024;

    public int DedupeCleanupScanInterval { get; init; } = 64;
}
