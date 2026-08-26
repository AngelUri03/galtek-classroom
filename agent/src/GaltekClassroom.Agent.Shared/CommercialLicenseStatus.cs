namespace GaltekClassroom.Agent.Shared;

public enum CommercialLicenseStatus
{
    Active,
    ActivationRequired,
    LicenseExpired,
    LicenseTampered,
    LicenseMalformed,
    LicenseInvalid,
    LicenseKeyNotConfigured,
    InstallationMismatch,
    HardwareMismatch,
    ProductMismatch,
    LicenseSchemaUnsupported,
    IssuerMismatch,
    AudienceMismatch,
    RoleNotAllowed
}

public static class CommercialLicenseStatusExtensions
{
    public static string ToCode(this CommercialLicenseStatus status)
    {
        return status switch
        {
            CommercialLicenseStatus.Active => "ACTIVE",
            CommercialLicenseStatus.ActivationRequired => "ACTIVATION_REQUIRED",
            CommercialLicenseStatus.LicenseExpired => "LICENSE_EXPIRED",
            CommercialLicenseStatus.LicenseTampered => "LICENSE_TAMPERED",
            CommercialLicenseStatus.LicenseMalformed => "LICENSE_MALFORMED",
            CommercialLicenseStatus.LicenseInvalid => "LICENSE_INVALID",
            CommercialLicenseStatus.LicenseKeyNotConfigured => "LICENSE_KEY_NOT_CONFIGURED",
            CommercialLicenseStatus.InstallationMismatch => "INSTALLATION_MISMATCH",
            CommercialLicenseStatus.HardwareMismatch => "HARDWARE_MISMATCH",
            CommercialLicenseStatus.ProductMismatch => "PRODUCT_MISMATCH",
            CommercialLicenseStatus.LicenseSchemaUnsupported => "LICENSE_SCHEMA_UNSUPPORTED",
            CommercialLicenseStatus.IssuerMismatch => "ISSUER_MISMATCH",
            CommercialLicenseStatus.AudienceMismatch => "AUDIENCE_MISMATCH",
            CommercialLicenseStatus.RoleNotAllowed => "ROLE_NOT_ALLOWED",
            _ => "LICENSE_INVALID"
        };
    }
}
