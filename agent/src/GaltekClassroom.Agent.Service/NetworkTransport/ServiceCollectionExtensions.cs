using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GaltekClassroom.Agent.Service.BrowserPolicy;
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
        services.AddSingleton<ChromiumBrowserPolicyCompiler>();
        services.AddSingleton<ChromiumBrowserPolicyEvaluator>();
        services.AddSingleton<ChromiumDownloadPolicyCompiler>();
        services.AddSingleton<IInteractiveUserIdentityResolver, WindowsInteractiveUserIdentityResolver>();
        services.AddSingleton<IBrowserPolicyRegistryStore, WindowsBrowserPolicyRegistryStore>();
        services.AddSingleton<IBrowserDownloadPolicyRegistryStore, WindowsBrowserDownloadPolicyRegistryStore>();
        services.AddSingleton(new BrowserNavigationPolicyStateStore(AgentDataDirectory.Resolve()));
        services.AddSingleton(new BrowserDownloadPolicyStateStore(AgentDataDirectory.Resolve()));
        services.AddSingleton<IBrowserNavigationOwnershipReader, BrowserNavigationOwnershipReader>();
        services.AddSingleton<BrowserNavigationPolicyApplyService>();
        services.AddSingleton<BrowserDownloadPolicyApplyService>();
        services.AddSingleton<AppliedBrowserPolicyEvaluator>();
        services.AddSingleton<IRemoteOperationHandler, ShutdownOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, RestartOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, OpenUrlOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, ApplyBrowserPolicyOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, ApplyBrowserDownloadPolicyOperationHandler>();
        services.AddSingleton<TrustedMasterResolver>();
        services.AddSingleton<MasterCertificatePinningPolicy>();
        services.AddSingleton<ClientHelloFactory>();
        services.AddSingleton<MasterConnectionStateTracker>();
        services.AddSingleton<RemoteOperationDispatcher>();
        services.AddSingleton<MasterGrpcConnectionClient>();

        return services;
    }
}
