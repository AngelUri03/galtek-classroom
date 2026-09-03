using GaltekClassroom.Agent.Service.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Applications;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationBindingServices(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var identityStoreOptions = provider.GetRequiredService<InstallationIdentityStoreOptions>();

            return new ApplicationBindingStoreOptions(identityStoreOptions.DataDirectory);
        });

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IApplicationBindingFileSecurity, WindowsApplicationBindingFileSecurity>();
        }
        else
        {
            services.AddSingleton<IApplicationBindingFileSecurity, NoOpApplicationBindingFileSecurity>();
        }

        services.AddSingleton<IApplicationBindingStore, ApplicationBindingStore>();
        services.AddSingleton<ApplicationBindingConfigurationService>();

        return services;
    }
}
