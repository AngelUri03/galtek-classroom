using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Identity;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInstallationIdentityServices(this IServiceCollection services)
    {
        services.AddSingleton(new InstallationIdentityStoreOptions(AgentDataDirectory.Resolve()));
        services.AddSingleton<InstallationIdentityStore>();
        services.AddSingleton<IHardwareFingerprintProvider, WindowsHardwareFingerprintProvider>();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<InstallationIdentityResolver>();
        services.AddSingleton<IHostNameProvider, SystemHostNameProvider>();
        services.AddSingleton<MachineCodeGenerator>();

        return services;
    }
}
