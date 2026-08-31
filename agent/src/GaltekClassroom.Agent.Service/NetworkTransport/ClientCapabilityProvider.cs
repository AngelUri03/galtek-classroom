using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class ClientCapabilityProvider
{
    private static readonly NetworkCapability[] Capabilities =
    [
        NetworkCapability.HeartbeatV1,
        NetworkCapability.OperationFrameworkV1,
        NetworkCapability.SessionAgentAvailable,
        NetworkCapability.PowerControlV1
    ];

    public IReadOnlyList<NetworkCapability> CurrentCapabilities()
    {
        return Capabilities;
    }
}
