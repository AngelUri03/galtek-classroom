namespace GaltekClassroom.Agent.Session.Lifecycle;

public sealed record SessionAgentSupervisorOptions
{
    public TimeSpan HealthyPollInterval { get; init; } = TimeSpan.FromSeconds(15);

    public IReadOnlyList<TimeSpan> ReconnectDelays { get; init; } =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];
}
