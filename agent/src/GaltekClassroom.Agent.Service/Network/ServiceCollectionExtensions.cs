using GaltekClassroom.Agent.Service.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Network;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNetworkIdentityServices(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var identityStoreOptions = provider.GetRequiredService<InstallationIdentityStoreOptions>();

            return new NetworkIdentityStoreOptions(identityStoreOptions.DataDirectory);
        });

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<INetworkIdentityFileSecurity, WindowsNetworkIdentityFileSecurity>();
            services.AddSingleton<INetworkIdentityKeyStore, WindowsCngNetworkIdentityKeyStore>();
        }
        else
        {
            services.AddSingleton<INetworkIdentityFileSecurity, NoOpNetworkIdentityFileSecurity>();
            services.AddSingleton<INetworkIdentityKeyStore, UnsupportedNetworkIdentityKeyStore>();
        }

        services.AddSingleton<NetworkIdentityStore>();
        services.AddSingleton<NetworkIdentityResolver>();
        services.AddSingleton<NetworkIdentityStatusService>();

        return services;
    }
}
