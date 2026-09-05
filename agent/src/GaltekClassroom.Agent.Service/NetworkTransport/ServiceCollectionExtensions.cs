using Microsoft.Extensions.Configuration;
using GaltekClassroom.Agent.Service.Applications;
using Microsoft.Extensions.DependencyInjection;
using GaltekClassroom.Agent.Service.BrowserPolicy;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.InputControl;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.OpenUrl;
using GaltekClassroom.Agent.Service.Power;
using GaltekClassroom.Agent.Service.WindowsSessions;

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
        services.AddSingleton<IWindowsConsoleSessionNativeApi, WindowsConsoleSessionNativeApi>();
        services.AddSingleton<IWindowsConsoleSessionResolver, WindowsConsoleSessionResolver>();
        services.AddSingleton<WindowsSessionStateService>();
        services.AddSingleton<IWindowsSessionLogoffNativeApi, WindowsSessionLogoffNativeApi>();
        services.AddSingleton<IWindowsSessionLogoffController, WindowsSessionLogoffController>();
        services.AddSingleton<WindowsSessionLogoffService>();
        services.AddSingleton<RemoteOperationLicensePolicy>();
        services.AddSingleton<IRemoteOperationHandler, ShutdownOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, RestartOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, OpenApplicationOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, OpenUrlOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, LockInputOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, UnlockInputOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, ApplyBrowserPolicyOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, ApplyBrowserDownloadPolicyOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, GetWindowsSessionStateOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, LogoffWindowsSessionOperationHandler>();
        services.AddSingleton<IRemoteOperationHandler, ProvisionManagedCredentialOperationHandler>();
        services.AddSingleton<TrustedMasterResolver>();
        services.AddSingleton<MasterCertificatePinningPolicy>();
        services.AddSingleton<ClientHelloFactory>();
        services.AddSingleton<MasterConnectionStateTracker>();
        services.AddSingleton<RemoteOperationDispatcher>();
        services.AddSingleton<MasterGrpcConnectionClient>();

        return services;
    }
}
