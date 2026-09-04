using GaltekClassroom.Agent.Service.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddManagedWindowsAccountBindingServices(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var identityStoreOptions = provider.GetRequiredService<InstallationIdentityStoreOptions>();

            return new ManagedWindowsAccountBindingStoreOptions(identityStoreOptions.DataDirectory);
        });

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IManagedWindowsAccountBindingFileSecurity, WindowsManagedWindowsAccountBindingFileSecurity>();
        }
        else
        {
            services.AddSingleton<IManagedWindowsAccountBindingFileSecurity, NoOpManagedWindowsAccountBindingFileSecurity>();
        }

        services.AddSingleton<IManagedWindowsAccountBindingStore, ManagedWindowsAccountBindingStore>();
        services.AddSingleton<ManagedWindowsAccountBindingConfigurationService>();

        return services;
    }
}
