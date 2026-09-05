using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.CredentialProviderBridge;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCredentialProviderBridgeServices(this IServiceCollection services)
    {
        services.AddSingleton(CredentialProviderBridgeServerOptions.Default);
        services.AddSingleton<ICredentialProviderActivationStore, CredentialProviderActivationStore>();
        services.AddSingleton<CredentialProviderActivationService>();
        services.AddSingleton<CredentialProviderBridgeRequestHandler>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ICredentialProviderBridgePipeStreamFactory, CredentialProviderBridgePipeStreamFactory>();
            services.AddSingleton<ICredentialProviderCallerPipeInspector, WindowsCredentialProviderCallerPipeInspector>();
            services.AddSingleton<ICredentialProviderCallerProcessInspector, WindowsCredentialProviderCallerProcessInspector>();
            services.AddSingleton<ICredentialProviderCallerVerifier>(provider =>
                new CredentialProviderCallerVerifier(
                    provider.GetRequiredService<ICredentialProviderCallerPipeInspector>(),
                    provider.GetRequiredService<ICredentialProviderCallerProcessInspector>(),
                    CredentialProviderCallerVerifier.DefaultExpectedLogonUiPath()));
            services.AddSingleton<CredentialProviderBridgeServer>();
        }
        else
        {
            services.AddSingleton<ICredentialProviderBridgePipeStreamFactory, UnsupportedCredentialProviderBridgePipeStreamFactory>();
            services.AddSingleton<ICredentialProviderCallerVerifier, UnavailableCredentialProviderCallerVerifier>();
            services.AddSingleton<NoOpCredentialProviderBridgeServer>();
        }

        return services;
    }
}
