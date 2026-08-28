namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class RemoteOperationOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
