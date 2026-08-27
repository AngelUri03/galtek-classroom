namespace GaltekClassroom.Agent.Service.Network;

public enum NetworkIdentityStatus
{
    NotConfigured,
    Ready,
    Invalid,
    KeyMissing,
    InstallationMismatch
}

public static class NetworkIdentityStatusExtensions
{
    public static string ToCode(this NetworkIdentityStatus status)
    {
        return status switch
        {
            NetworkIdentityStatus.NotConfigured => "NOT_CONFIGURED",
            NetworkIdentityStatus.Ready => "READY",
            NetworkIdentityStatus.Invalid => "INVALID",
            NetworkIdentityStatus.KeyMissing => "KEY_MISSING",
            NetworkIdentityStatus.InstallationMismatch => "INSTALLATION_MISMATCH",
            _ => "INVALID"
        };
    }

    public static string? ToErrorCode(this NetworkIdentityStatus status)
    {
        return status switch
        {
            NetworkIdentityStatus.NotConfigured => NetworkIdentityConstants.NotConfiguredErrorCode,
            NetworkIdentityStatus.Ready => null,
            NetworkIdentityStatus.Invalid => NetworkIdentityConstants.InvalidErrorCode,
            NetworkIdentityStatus.KeyMissing => NetworkIdentityConstants.KeyMissingErrorCode,
            NetworkIdentityStatus.InstallationMismatch => NetworkIdentityConstants.InstallationMismatchErrorCode,
            _ => NetworkIdentityConstants.InvalidErrorCode
        };
    }
}
