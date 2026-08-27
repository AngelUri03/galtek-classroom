using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Pairing;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClientPairingServices(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var identityStoreOptions = provider.GetRequiredService<InstallationIdentityStoreOptions>();

            return new ClientTrustStoreOptions(identityStoreOptions.DataDirectory);
        });

        services.AddSingleton<ClientTrustStore>();
        services.AddSingleton<ClientPairingService>();

        return services;
    }
}
