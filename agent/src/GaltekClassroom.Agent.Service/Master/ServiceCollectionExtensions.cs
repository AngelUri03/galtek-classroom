using GaltekClassroom.Agent.Service.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Master;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMasterAuthorizationServices(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var identityStoreOptions = provider.GetRequiredService<InstallationIdentityStoreOptions>();

            return new MasterBindingStoreOptions(identityStoreOptions.DataDirectory);
        });

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IMasterBindingFileSecurity, WindowsMasterBindingFileSecurity>();
            services.AddSingleton<IWindowsAccountResolver, WindowsAccountResolver>();
            services.AddSingleton<IAdministratorPrivilegeChecker, WindowsAdministratorPrivilegeChecker>();
        }
        else
        {
            services.AddSingleton<IMasterBindingFileSecurity, NoOpMasterBindingFileSecurity>();
            services.AddSingleton<IWindowsAccountResolver, UnsupportedWindowsAccountResolver>();
            services.AddSingleton<IAdministratorPrivilegeChecker, UnsupportedAdministratorPrivilegeChecker>();
        }

        services.AddSingleton<MasterBindingStore>();
        services.AddSingleton<MasterAuthorizationService>();
        services.AddSingleton<MasterBindingConfigurationService>();

        return services;
    }
}
