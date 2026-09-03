using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class RemoteOperationLicensePolicy
{
    public static RemoteOperationLicensePolicy Default { get; } = new();

    public bool RequiresActiveCommercialLicense(NetworkOperationType operationType)
    {
        return operationType != NetworkOperationType.UnlockInput;
    }
}
