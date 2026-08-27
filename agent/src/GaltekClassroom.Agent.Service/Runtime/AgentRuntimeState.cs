using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Agent.Service.Network;

namespace GaltekClassroom.Agent.Service.Runtime;

public sealed class AgentRuntimeState
{
    private readonly object _sync = new();
    private InstallationIdentity? _installationIdentity;
    private NetworkIdentityMetadata? _networkIdentity;

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

    public void SetNetworkIdentity(NetworkIdentityMetadata networkIdentity)
    {
        ArgumentNullException.ThrowIfNull(networkIdentity);

        lock (_sync)
        {
            _networkIdentity = networkIdentity;
        }
    }

    public NetworkIdentityMetadata GetNetworkIdentity()
    {
        lock (_sync)
        {
            return _networkIdentity
                ?? throw new InvalidOperationException("Network identity has not been resolved yet.");
        }
    }
}
