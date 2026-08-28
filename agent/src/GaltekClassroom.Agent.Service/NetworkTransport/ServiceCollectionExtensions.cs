using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMasterNetworkTransportServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(MasterConnectionOptions.FromConfiguration(configuration));
        services.AddSingleton<TrustedMasterResolver>();
        services.AddSingleton<MasterCertificatePinningPolicy>();
        services.AddSingleton<ClientHelloFactory>();
        services.AddSingleton<MasterConnectionStateTracker>();
        services.AddSingleton<MasterGrpcConnectionClient>();

        return services;
    }
}
