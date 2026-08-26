using GaltekClassroom.Agent.Service.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Licensing;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommercialLicenseServices(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var identityStoreOptions = provider.GetRequiredService<InstallationIdentityStoreOptions>();

            return new CommercialLicenseStoreOptions(identityStoreOptions.DataDirectory);
        });
        services.AddSingleton<CommercialLicenseStore>();
        services.AddSingleton<ILicensePublicKeyProvider, LicensePublicKeyProvider>();
        services.AddSingleton<CommercialLicenseValidator>();
        services.AddSingleton<CommercialLicenseManager>();
        services.AddSingleton<CommercialLicenseActivationService>();

        return services;
    }
}
