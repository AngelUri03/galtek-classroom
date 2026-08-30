namespace GaltekClassroom.Agent.Service.Runtime;

public enum AgentStartupPhase
{
    Starting,
    Recovering,
    MinimalReady,
    SecurityReady,
    NetworkReady,
    OperationReady,
    Degraded
}

public static class AgentStartupPhaseExtensions
{
    public static string ToCode(this AgentStartupPhase phase)
    {
        return phase switch
        {
            AgentStartupPhase.Starting => "STARTING",
            AgentStartupPhase.Recovering => "RECOVERING",
            AgentStartupPhase.MinimalReady => "MINIMAL_READY",
            AgentStartupPhase.SecurityReady => "SECURITY_READY",
            AgentStartupPhase.NetworkReady => "NETWORK_READY",
            AgentStartupPhase.OperationReady => "OPERATION_READY",
            AgentStartupPhase.Degraded => "DEGRADED",
            _ => "STARTING"
        };
    }
}

public sealed record AgentRuntimeSnapshot(
    AgentStartupPhase StartupPhase,
    bool PreviousShutdownWasUnclean,
    bool RecoveryActive);
