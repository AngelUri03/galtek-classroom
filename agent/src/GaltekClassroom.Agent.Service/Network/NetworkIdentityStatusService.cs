using GaltekClassroom.Agent.Service.Identity;

namespace GaltekClassroom.Agent.Service.Network;

public sealed class NetworkIdentityStatusService
{
    private readonly InstallationIdentityStore _installationIdentityStore;
    private readonly NetworkIdentityStore _networkIdentityStore;
    private readonly NetworkIdentityResolver _networkIdentityResolver;

    public NetworkIdentityStatusService(
        InstallationIdentityStore installationIdentityStore,
        NetworkIdentityStore networkIdentityStore,
        NetworkIdentityResolver networkIdentityResolver)
    {
        _installationIdentityStore = installationIdentityStore;
        _networkIdentityStore = networkIdentityStore;
        _networkIdentityResolver = networkIdentityResolver;
    }

    public async Task<NetworkIdentityResolution> GetStatusAsync(CancellationToken cancellationToken)
    {
        var installationRead = await _installationIdentityStore.ReadAsync(cancellationToken);

        if (installationRead.Status == InstallationIdentityStoreReadStatus.Loaded)
        {
            return await _networkIdentityResolver.GetStatusAsync(
                installationRead.Identity!,
                cancellationToken);
        }

        var networkRead = await _networkIdentityStore.ReadAsync(cancellationToken);
        if (networkRead.Status == NetworkIdentityStoreReadStatus.Missing)
        {
            return NetworkIdentityResolution.NotConfigured(networkRead.FilePath);
        }

        if (networkRead.Status == NetworkIdentityStoreReadStatus.Invalid)
        {
            return NetworkIdentityResolution.Invalid(
                networkRead.FilePath,
                networkRead.ErrorMessage ?? "network-identity.json is invalid");
        }

        return NetworkIdentityResolution.Invalid(
            networkRead.FilePath,
            installationRead.ErrorMessage
                ?? "installation.json is missing; network identity cannot be evaluated.");
    }
}
