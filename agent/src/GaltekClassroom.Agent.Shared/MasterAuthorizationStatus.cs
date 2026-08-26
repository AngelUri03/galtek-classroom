namespace GaltekClassroom.Agent.Shared;

public enum MasterAuthorizationStatus
{
    NotConfigured,
    Authorized,
    CurrentAccountNotAuthorized,
    MasterLicenseRequired,
    MasterBindingInvalid,
    InstallationMismatch
}

public static class MasterAuthorizationStatusExtensions
{
    public static string ToCode(this MasterAuthorizationStatus status)
    {
        return status switch
        {
            MasterAuthorizationStatus.NotConfigured => "NOT_CONFIGURED",
            MasterAuthorizationStatus.Authorized => "AUTHORIZED",
            MasterAuthorizationStatus.CurrentAccountNotAuthorized => "CURRENT_ACCOUNT_NOT_AUTHORIZED",
            MasterAuthorizationStatus.MasterLicenseRequired => "MASTER_LICENSE_REQUIRED",
            MasterAuthorizationStatus.MasterBindingInvalid => "MASTER_BINDING_INVALID",
            MasterAuthorizationStatus.InstallationMismatch => "MASTER_BINDING_INSTALLATION_MISMATCH",
            _ => "MASTER_BINDING_INVALID"
        };
    }
}

public static class MasterBindingConstants
{
    public const int SchemaVersion = 1;
    public const string FileName = "master-binding.json";
}
