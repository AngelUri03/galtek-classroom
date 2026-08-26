using GaltekClassroom.Agent.Service.Identity;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed class CommercialLicenseActivationService
{
    private readonly InstallationIdentityResolver _installationIdentityResolver;
    private readonly CommercialLicenseManager _licenseManager;

    public CommercialLicenseActivationService(
        InstallationIdentityResolver installationIdentityResolver,
        CommercialLicenseManager licenseManager)
    {
        _installationIdentityResolver = installationIdentityResolver;
        _licenseManager = licenseManager;
    }

    public async Task<CommercialLicenseActivationResult> ActivateAsync(
        string candidateToken,
        CancellationToken cancellationToken)
    {
        var installationResolution = await _installationIdentityResolver.ResolveAsync(cancellationToken);

        if (installationResolution.Status != InstallationIdentityResolutionStatus.Ready)
        {
            throw new InvalidOperationException(
                $"Installation identity is not usable at {installationResolution.FilePath}: {installationResolution.ErrorMessage}");
        }

        return await _licenseManager.ActivateAsync(
            candidateToken,
            installationResolution.Identity!,
            cancellationToken);
    }
}
