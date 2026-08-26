using Microsoft.IdentityModel.Tokens;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed record LicensePublicKeyResult(
    bool IsConfigured,
    SecurityKey? SecurityKey,
    string? ErrorMessage)
{
    public static LicensePublicKeyResult Configured(SecurityKey securityKey)
    {
        ArgumentNullException.ThrowIfNull(securityKey);

        return new LicensePublicKeyResult(true, securityKey, null);
    }

    public static LicensePublicKeyResult NotConfigured(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new LicensePublicKeyResult(false, null, errorMessage);
    }
}

public interface ILicensePublicKeyProvider
{
    Task<LicensePublicKeyResult> GetPublicKeyAsync(CancellationToken cancellationToken);
}
