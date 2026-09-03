namespace GaltekClassroom.Agent.Service;

public enum AgentCommandMode
{
    Service,
    MachineCode,
    LicenseStatus,
    ActivateLicenseFromStdin,
    ActivateLicenseFromFile,
    BindMasterCurrentUser,
    BindMasterAccount,
    NetworkIdentityStatus,
    RuntimeDiagnostics,
    ApplicationBindList,
    ApplicationBindExe,
    ApplicationBindAppPath,
    ApplicationBindDisable,
    ApplicationBindEnable,
    ApplicationBindRemove
}

public sealed record AgentCommandLine(
    AgentCommandMode Mode,
    string[] HostArgs,
    string? LicenseFilePath,
    string? MasterAccountName,
    string? ApplicationId,
    string? ApplicationTarget,
    bool ReplaceMasterBinding,
    bool ReplaceApplicationBinding,
    string? ErrorMessage)
{
    public bool IsValid => ErrorMessage is null;

    public static AgentCommandLine Parse(string[] args)
    {
        const string machineCodeArgument = "--machine-code";
        const string licenseStatusArgument = "--license-status";
        const string activateLicenseArgument = "--activate-license";
        const string activateLicenseFileArgument = "--activate-license-file";
        const string bindMasterCurrentUserArgument = "--bind-master-current-user";
        const string bindMasterAccountArgument = "--bind-master-account";
        const string replaceMasterBindingArgument = "--replace-master-binding";
        const string networkIdentityStatusArgument = "--network-identity-status";
        const string runtimeDiagnosticsArgument = "--runtime-diagnostics";
        const string applicationBindListArgument = "--application-bind-list";
        const string applicationBindExeArgument = "--application-bind-exe";
        const string applicationBindAppPathArgument = "--application-bind-app-path";
        const string applicationBindDisableArgument = "--application-bind-disable";
        const string applicationBindEnableArgument = "--application-bind-enable";
        const string applicationBindRemoveArgument = "--application-bind-remove";
        const string replaceApplicationBindingArgument = "--replace-application-binding";

        var mode = AgentCommandMode.Service;
        var hostArgs = new List<string>();
        string? licenseFilePath = null;
        string? masterAccountName = null;
        string? applicationId = null;
        string? applicationTarget = null;
        var replaceMasterBinding = false;
        var replaceApplicationBinding = false;
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

            if (string.Equals(argument, bindMasterCurrentUserArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.BindMasterCurrentUser, argument, ref mode, ref error);
                continue;
            }

            if (string.Equals(argument, bindMasterAccountArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.BindMasterAccount, argument, ref mode, ref error);

                if (index + 1 >= args.Length)
                {
                    error ??= "--bind-master-account requires a Windows account name.";
                    continue;
                }

                masterAccountName = args[++index];
                continue;
            }

            if (string.Equals(argument, replaceMasterBindingArgument, StringComparison.OrdinalIgnoreCase))
            {
                replaceMasterBinding = true;
                continue;
            }

            if (string.Equals(argument, networkIdentityStatusArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.NetworkIdentityStatus, argument, ref mode, ref error);
                continue;
            }

            if (string.Equals(argument, runtimeDiagnosticsArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.RuntimeDiagnostics, argument, ref mode, ref error);
                continue;
            }

            if (string.Equals(argument, applicationBindListArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.ApplicationBindList, argument, ref mode, ref error);
                continue;
            }

            if (string.Equals(argument, applicationBindExeArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.ApplicationBindExe, argument, ref mode, ref error);

                if (index + 2 >= args.Length)
                {
                    error ??= "--application-bind-exe requires an applicationId and absolute .exe path.";
                    continue;
                }

                applicationId = args[++index];
                applicationTarget = args[++index];
                continue;
            }

            if (string.Equals(argument, applicationBindAppPathArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.ApplicationBindAppPath, argument, ref mode, ref error);

                if (index + 2 >= args.Length)
                {
                    error ??= "--application-bind-app-path requires an applicationId and executable name.";
                    continue;
                }

                applicationId = args[++index];
                applicationTarget = args[++index];
                continue;
            }

            if (string.Equals(argument, applicationBindDisableArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.ApplicationBindDisable, argument, ref mode, ref error);

                if (index + 1 >= args.Length)
                {
                    error ??= "--application-bind-disable requires an applicationId.";
                    continue;
                }

                applicationId = args[++index];
                continue;
            }

            if (string.Equals(argument, applicationBindEnableArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.ApplicationBindEnable, argument, ref mode, ref error);

                if (index + 1 >= args.Length)
                {
                    error ??= "--application-bind-enable requires an applicationId.";
                    continue;
                }

                applicationId = args[++index];
                continue;
            }

            if (string.Equals(argument, applicationBindRemoveArgument, StringComparison.OrdinalIgnoreCase))
            {
                SetMode(AgentCommandMode.ApplicationBindRemove, argument, ref mode, ref error);

                if (index + 1 >= args.Length)
                {
                    error ??= "--application-bind-remove requires an applicationId.";
                    continue;
                }

                applicationId = args[++index];
                continue;
            }

            if (string.Equals(argument, replaceApplicationBindingArgument, StringComparison.OrdinalIgnoreCase))
            {
                replaceApplicationBinding = true;
                continue;
            }

            hostArgs.Add(argument);
        }

        if (replaceMasterBinding
            && mode is not AgentCommandMode.BindMasterCurrentUser and not AgentCommandMode.BindMasterAccount)
        {
            error ??= "--replace-master-binding can only be used with a Master binding command.";
        }

        if (replaceApplicationBinding
            && mode is not AgentCommandMode.ApplicationBindExe and not AgentCommandMode.ApplicationBindAppPath)
        {
            error ??= "--replace-application-binding can only be used with an application binding command.";
        }

        return new AgentCommandLine(
            mode,
            hostArgs.ToArray(),
            licenseFilePath,
            masterAccountName,
            applicationId,
            applicationTarget,
            replaceMasterBinding,
            replaceApplicationBinding,
            error);
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
