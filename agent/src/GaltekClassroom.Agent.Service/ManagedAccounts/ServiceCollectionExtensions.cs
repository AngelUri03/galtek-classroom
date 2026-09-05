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
        services.AddSingleton(provider =>
        {
            var identityStoreOptions = provider.GetRequiredService<InstallationIdentityStoreOptions>();

            return new ManagedWindowsCredentialStoreOptions(identityStoreOptions.DataDirectory);
        });

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IManagedWindowsAccountBindingFileSecurity, WindowsManagedWindowsAccountBindingFileSecurity>();
            services.AddSingleton<IManagedWindowsCredentialFileSecurity, WindowsManagedWindowsCredentialFileSecurity>();
            services.AddSingleton<IWindowsDpapiManagedWindowsCredentialNativeApi, WindowsDpapiManagedWindowsCredentialNativeApi>();
            services.AddSingleton<IManagedWindowsCredentialProtector, WindowsDpapiManagedWindowsCredentialProtector>();
        }
        else
        {
            services.AddSingleton<IManagedWindowsAccountBindingFileSecurity, NoOpManagedWindowsAccountBindingFileSecurity>();
            services.AddSingleton<IManagedWindowsCredentialFileSecurity, NoOpManagedWindowsCredentialFileSecurity>();
            services.AddSingleton<IManagedWindowsCredentialProtector, UnsupportedManagedWindowsCredentialProtector>();
        }

        services.AddSingleton<IManagedWindowsAccountBindingStore, ManagedWindowsAccountBindingStore>();
        services.AddSingleton<IManagedWindowsCredentialStore, ManagedWindowsCredentialStore>();
        services.AddSingleton<ManagedWindowsAccountBindingConfigurationService>();

        return services;
    }
}
