using System.Collections.ObjectModel;

namespace GaltekClassroom.Agent.Shared;

public sealed record LicenseState
{
    public CommercialLicenseStatus Status { get; init; }
    public bool Active => Status == CommercialLicenseStatus.Active;
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? LicenseId { get; init; }
    public string? OrganizationId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public CommercialLicenseFeatures Features { get; init; } = CommercialLicenseFeatures.Empty;
    public DateTimeOffset LastValidatedAtUtc { get; init; }
    public string? BlockingReason { get; init; }

    public static LicenseState ActiveState(
        string licenseId,
        string? organizationId,
        IReadOnlyList<string> roles,
        CommercialLicenseFeatures features,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset lastValidatedAtUtc)
    {
        return new LicenseState
        {
            Status = CommercialLicenseStatus.Active,
            ExpiresAtUtc = expiresAtUtc.ToUniversalTime(),
            LicenseId = licenseId,
            OrganizationId = organizationId,
            Roles = CopyList(roles),
            Features = features,
            LastValidatedAtUtc = lastValidatedAtUtc.ToUniversalTime()
        };
    }

    public static LicenseState Blocked(
        CommercialLicenseStatus status,
        DateTimeOffset lastValidatedAtUtc,
        string blockingReason,
        DateTimeOffset? expiresAtUtc = null,
        string? licenseId = null,
        string? organizationId = null,
        IReadOnlyList<string>? roles = null,
        CommercialLicenseFeatures? features = null)
    {
        if (status == CommercialLicenseStatus.Active)
        {
            throw new ArgumentException("Use ActiveState for active licenses.", nameof(status));
        }

        return new LicenseState
        {
            Status = status,
            ExpiresAtUtc = expiresAtUtc?.ToUniversalTime(),
            LicenseId = licenseId,
            OrganizationId = organizationId,
            Roles = CopyList(roles ?? Array.Empty<string>()),
            Features = features ?? CommercialLicenseFeatures.Empty,
            LastValidatedAtUtc = lastValidatedAtUtc.ToUniversalTime(),
            BlockingReason = blockingReason
        };
    }

    private static ReadOnlyCollection<string> CopyList(IReadOnlyList<string> values)
    {
        return new ReadOnlyCollection<string>(values.ToArray());
    }
}
