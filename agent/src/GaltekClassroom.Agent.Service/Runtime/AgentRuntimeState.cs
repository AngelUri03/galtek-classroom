using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Runtime;

public sealed class AgentRuntimeState
{
    private readonly object _sync = new();
    private InstallationIdentity? _installationIdentity;

    public void SetInstallationIdentity(InstallationIdentity installationIdentity)
    {
        ArgumentNullException.ThrowIfNull(installationIdentity);

        lock (_sync)
        {
            _installationIdentity = installationIdentity;
        }
    }

    public InstallationIdentity GetInstallationIdentity()
    {
        lock (_sync)
        {
            return _installationIdentity
                ?? throw new InvalidOperationException("Installation identity has not been resolved yet.");
        }
    }
}
