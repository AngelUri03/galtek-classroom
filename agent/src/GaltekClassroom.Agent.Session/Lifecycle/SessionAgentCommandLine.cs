namespace GaltekClassroom.Agent.Session.Lifecycle;

public enum SessionAgentCommandMode
{
    Background,
    IpcPing,
    IpcStatus,
    RuntimeDiagnostics,
    AgentRuntimeDiagnostics
}

public sealed record SessionAgentCommandLine(
    SessionAgentCommandMode Mode,
    string? ErrorMessage)
{
    public bool IsValid => ErrorMessage is null;

    public bool IsOneShotIpcCommand =>
        Mode is SessionAgentCommandMode.IpcPing
            or SessionAgentCommandMode.IpcStatus
            or SessionAgentCommandMode.AgentRuntimeDiagnostics;

    public static SessionAgentCommandLine Parse(string[] args)
    {
        const string backgroundArgument = "--background";
        const string ipcPingArgument = "--ipc-ping";
        const string ipcStatusArgument = "--ipc-status";
        const string runtimeDiagnosticsArgument = "--runtime-diagnostics";
        const string agentRuntimeDiagnosticsArgument = "--agent-runtime-diagnostics";

        var mode = SessionAgentCommandMode.Background;
        var explicitModeSet = false;
        string? error = null;

        foreach (var argument in args)
        {
            if (string.Equals(argument, backgroundArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(SessionAgentCommandMode.Background, argument, ref mode, ref explicitModeSet, ref error);
                continue;
            }

            if (string.Equals(argument, ipcPingArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(SessionAgentCommandMode.IpcPing, argument, ref mode, ref explicitModeSet, ref error);
                continue;
            }

            if (string.Equals(argument, ipcStatusArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(SessionAgentCommandMode.IpcStatus, argument, ref mode, ref explicitModeSet, ref error);
                continue;
            }

            if (string.Equals(argument, runtimeDiagnosticsArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(SessionAgentCommandMode.RuntimeDiagnostics, argument, ref mode, ref explicitModeSet, ref error);
                continue;
            }

            if (string.Equals(argument, agentRuntimeDiagnosticsArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(
                    SessionAgentCommandMode.AgentRuntimeDiagnostics,
                    argument,
                    ref mode,
                    ref explicitModeSet,
                    ref error);
                continue;
            }

            error ??= $"Unknown argument: {argument}.";
        }

        return new SessionAgentCommandLine(mode, error);
    }

    private static void SetMode(
        SessionAgentCommandMode requestedMode,
        string argument,
        ref SessionAgentCommandMode currentMode,
        ref bool explicitModeSet,
        ref string? error)
    {
        if (explicitModeSet && currentMode != requestedMode)
        {
            error ??= $"Only one command mode can be used. Conflicting argument: {argument}.";
            return;
        }

        currentMode = requestedMode;
        explicitModeSet = true;
    }
}
