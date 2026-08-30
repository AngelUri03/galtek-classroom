using GaltekClassroom.Agent.Service.Runtime;
using GaltekClassroom.Agent.Service.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Ipc;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLocalIpcServices(this IServiceCollection services)
    {
        services.AddSingleton<AgentRuntimeState>();
        services.AddSingleton(provider =>
        {
            var identityStoreOptions = provider.GetRequiredService<InstallationIdentityStoreOptions>();
            return new ServiceRunMarkerOptions(
                identityStoreOptions.DataDirectory,
                ServiceRunMarker.DefaultFileName);
        });
        services.AddSingleton<ServiceRunMarker>();
        services.AddSingleton(LocalIpcServerOptions.Default);

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ILocalIpcPipeStreamFactory, LocalIpcPipeStreamFactory>();
            services.AddSingleton<ILocalIpcClientIdentityProvider, WindowsLocalIpcClientIdentityProvider>();
        }
        else
        {
            services.AddSingleton<ILocalIpcPipeStreamFactory, UnsupportedLocalIpcPipeStreamFactory>();
            services.AddSingleton<ILocalIpcClientIdentityProvider, UnavailableLocalIpcClientIdentityProvider>();
        }

        services.AddSingleton<ILocalIpcRequestHandler, LocalIpcRequestHandler>();

        return services;
    }
}
