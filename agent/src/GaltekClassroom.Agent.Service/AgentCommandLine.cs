namespace GaltekClassroom.Agent.Service;

public enum AgentCommandMode
{
    Service,
    MachineCode,
    LicenseStatus,
    ActivateLicenseFromStdin,
    ActivateLicenseFromFile
}

public sealed record AgentCommandLine(
    AgentCommandMode Mode,
    string[] HostArgs,
    string? LicenseFilePath,
    string? ErrorMessage)
{
    public bool IsValid => ErrorMessage is null;

    public static AgentCommandLine Parse(string[] args)
    {
        const string machineCodeArgument = "--machine-code";
        const string licenseStatusArgument = "--license-status";
        const string activateLicenseArgument = "--activate-license";
        const string activateLicenseFileArgument = "--activate-license-file";

        var mode = AgentCommandMode.Service;
        var hostArgs = new List<string>();
        string? licenseFilePath = null;
        string? error = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (string.Equals(argument, machineCodeArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.MachineCode, argument, ref mode, ref error);
                continue;
            }

            if (string.Equals(argument, licenseStatusArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.LicenseStatus, argument, ref mode, ref error);
                continue;
            }

            if (string.Equals(argument, activateLicenseArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.ActivateLicenseFromStdin, argument, ref mode, ref error);
                continue;
            }

            if (string.Equals(argument, activateLicenseFileArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.ActivateLicenseFromFile, argument, ref mode, ref error);

                if (index + 1 >= args.Length)
                {
                    error ??= "--activate-license-file requires a file path.";
                    continue;
                }

                licenseFilePath = args[++index];
                continue;
            }

            hostArgs.Add(argument);
        }

        return new AgentCommandLine(mode, hostArgs.ToArray(), licenseFilePath, error);
    }

    private static void SetMode(
        AgentCommandMode requestedMode,
        string argument,
        ref AgentCommandMode currentMode,
        ref string? error)
    {
        if (currentMode != AgentCommandMode.Service && currentMode != requestedMode)
        {
            error ??= $"Only one command mode can be used. Conflicting argument: {argument}.";
            return;
        }

        currentMode = requestedMode;
    }
}
