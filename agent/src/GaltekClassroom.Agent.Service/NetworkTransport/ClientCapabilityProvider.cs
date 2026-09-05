using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class ClientCapabilityProvider
{
    private static readonly NetworkCapability[] Capabilities =
    [
        NetworkCapability.HeartbeatV1,
        NetworkCapability.OperationFrameworkV1,
        NetworkCapability.SessionAgentAvailable,
        NetworkCapability.PowerControlV1,
        NetworkCapability.OpenApplicationV1,
        NetworkCapability.OpenUrlV1,
        NetworkCapability.BrowserNavigationPolicyV1,
        NetworkCapability.BrowserDownloadPolicyV1,
        NetworkCapability.InputControlV1,
        NetworkCapability.WindowsSessionStateV1,
        NetworkCapability.WindowsSessionLogoffV1,
        NetworkCapability.ManagedCredentialProvisioningV1
    ];

    public IReadOnlyList<NetworkCapability> CurrentCapabilities()
    {
        return Capabilities;
    }
}
