using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.OpenUrl;
using GaltekClassroom.Agent.Service.Power;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMasterNetworkTransportServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(MasterConnectionOptions.FromConfiguration(configuration));
        services.AddSingleton(new RemoteOperationOptions());
        services.AddSingleton<ClientCapabilityProvider>();
        services.AddSingleton<AgentVersionProvider>();
        services.AddSingleton<IReconnectJitter, RandomReconnectJitter>();
        services.AddSingleton<IWindowsPowerController, WindowsPowerController>();
        services.AddSingleton(new PowerOperationReceiptStoreOptions(AgentDataDirectory.Resolve()));
        services.AddSingleton<PowerOperationReceiptStore>();
        services.AddSingleton<IRemoteOperationHandler, ShutdownOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, RestartOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, OpenUrlOperationHandler>();
        services.AddSingleton<TrustedMasterResolver>();
        services.AddSingleton<MasterCertificatePinningPolicy>();
        services.AddSingleton<ClientHelloFactory>();
        services.AddSingleton<MasterConnectionStateTracker>();
        services.AddSingleton<RemoteOperationDispatcher>();
        services.AddSingleton<MasterGrpcConnectionClient>();

        return services;
    }
}
