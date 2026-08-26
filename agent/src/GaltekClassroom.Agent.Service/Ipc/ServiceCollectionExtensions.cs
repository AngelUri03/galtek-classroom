using GaltekClassroom.Agent.Service.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Ipc;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLocalIpcServices(this IServiceCollection services)
    {
        services.AddSingleton<AgentRuntimeState>();
        services.AddSingleton(LocalIpcServerOptions.Default);

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ILocalIpcPipeStreamFactory, LocalIpcPipeStreamFactory>();
        }
        else
        {
            services.AddSingleton<ILocalIpcPipeStreamFactory, UnsupportedLocalIpcPipeStreamFactory>();
        }

        services.AddSingleton<ILocalIpcRequestHandler, LocalIpcRequestHandler>();

        return services;
    }
}
