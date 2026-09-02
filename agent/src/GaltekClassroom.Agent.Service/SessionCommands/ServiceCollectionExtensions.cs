using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.SessionCommands;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSessionCommandServices(this IServiceCollection services)
    {
        services.AddSingleton(SessionCommandClientOptions.Default);

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IInteractiveSessionResolver, WindowsInteractiveSessionResolver>();
            services.AddSingleton<ISessionAgentServerVerifier, WindowsSessionAgentServerVerifier>();
        }
        else
        {
            services.AddSingleton<IInteractiveSessionResolver, UnavailableInteractiveSessionResolver>();
            services.AddSingleton<ISessionAgentServerVerifier, UnavailableSessionAgentServerVerifier>();
        }

        services.AddSingleton<SessionCommandClient>();
        services.AddSingleton<ISessionCommandClient>(provider =>
            provider.GetRequiredService<SessionCommandClient>());

        return services;
    }
}
