using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Network;

public sealed record NetworkIdentityResolution(
    NetworkIdentityStatus Status,
    NetworkIdentityMetadata? Metadata,
    string FilePath,
    string? ErrorMessage,
    bool Created)
{
    public bool Ready => Status == NetworkIdentityStatus.Ready;

    public string? ErrorCode => Status.ToErrorCode();

    public static NetworkIdentityResolution NotConfigured(string filePath)
    {
        return new NetworkIdentityResolution(
            NetworkIdentityStatus.NotConfigured,
            null,
            filePath,
            "network-identity.json is not configured",
            Created: false);
    }

    public static NetworkIdentityResolution ReadyResult(
        NetworkIdentityMetadata metadata,
        string filePath,
        bool created)
    {
        return new NetworkIdentityResolution(
            NetworkIdentityStatus.Ready,
            metadata,
            filePath,
            null,
            created);
    }

    public static NetworkIdentityResolution Invalid(
        string filePath,
        string errorMessage,
        NetworkIdentityMetadata? metadata = null)
    {
        return new NetworkIdentityResolution(
            NetworkIdentityStatus.Invalid,
            metadata,
            filePath,
            errorMessage,
            Created: false);
    }

    public static NetworkIdentityResolution KeyMissing(
        string filePath,
        NetworkIdentityMetadata metadata,
        string errorMessage)
    {
        return new NetworkIdentityResolution(
            NetworkIdentityStatus.KeyMissing,
            metadata,
            filePath,
            errorMessage,
            Created: false);
    }

    public static NetworkIdentityResolution InstallationMismatch(
        string filePath,
        NetworkIdentityMetadata metadata,
        Guid expectedInstallationId)
    {
        return new NetworkIdentityResolution(
            NetworkIdentityStatus.InstallationMismatch,
            metadata,
            filePath,
            $"network identity belongs to installation {metadata.InstallationId:D}, expected {expectedInstallationId:D}",
            Created: false);
    }
}

public sealed class NetworkIdentityResolver
{
    private readonly NetworkIdentityStore _store;
    private readonly INetworkIdentityKeyStore _keyStore;
    private readonly ISystemClock _clock;

    public NetworkIdentityResolver(
        NetworkIdentityStore store,
        INetworkIdentityKeyStore keyStore,
        ISystemClock clock)
    {
        _store = store;
        _keyStore = keyStore;
        _clock = clock;
    }

    public Task<NetworkIdentityResolution> ResolveAsync(
        InstallationIdentity installationIdentity,
        CancellationToken cancellationToken)
    {
        return ResolveCoreAsync(installationIdentity, createIfMissing: true, cancellationToken);
    }

    public Task<NetworkIdentityResolution> GetStatusAsync(
        InstallationIdentity installationIdentity,
        CancellationToken cancellationToken)
    {
        return ResolveCoreAsync(installationIdentity, createIfMissing: false, cancellationToken);
    }

    private async Task<NetworkIdentityResolution> ResolveCoreAsync(
        InstallationIdentity installationIdentity,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installationIdentity);

        var readResult = await _store.ReadAsync(cancellationToken);

        if (readResult.Status == NetworkIdentityStoreReadStatus.Loaded)
        {
            return ValidateLoadedIdentity(
                installationIdentity,
                readResult.Metadata!,
                readResult.FilePath);
        }

        if (readResult.Status == NetworkIdentityStoreReadStatus.Invalid)
        {
            return NetworkIdentityResolution.Invalid(
                readResult.FilePath,
                readResult.ErrorMessage ?? "network-identity.json is invalid");
        }

        var descriptor = NetworkIdentityKeyDescriptor.ForInstallation(installationIdentity.InstallationId);
        if (_keyStore.Exists(descriptor.KeyName))
        {
            return NetworkIdentityResolution.Invalid(
                readResult.FilePath,
                "network-identity.json is missing but its Galtek CNG key already exists; refusing to regenerate silently.");
        }

        if (!createIfMissing)
        {
            return NetworkIdentityResolution.NotConfigured(readResult.FilePath);
        }

        return await CreateNewIdentityAsync(
            installationIdentity,
            descriptor,
            readResult.FilePath,
            cancellationToken);
    }

    private NetworkIdentityResolution ValidateLoadedIdentity(
        InstallationIdentity installationIdentity,
        NetworkIdentityMetadata metadata,
        string filePath)
    {
        if (metadata.InstallationId != installationIdentity.InstallationId)
        {
            return NetworkIdentityResolution.InstallationMismatch(
                filePath,
                metadata,
                installationIdentity.InstallationId);
        }

        var expectedDescriptor = NetworkIdentityKeyDescriptor.ForInstallation(installationIdentity.InstallationId);
        if (!string.Equals(metadata.KeyId, expectedDescriptor.KeyId, StringComparison.Ordinal)
            || !string.Equals(metadata.KeyName, expectedDescriptor.KeyName, StringComparison.Ordinal))
        {
            return NetworkIdentityResolution.Invalid(
                filePath,
                "network identity key metadata is incompatible with the current installation",
                metadata);
        }

        var lookup = _keyStore.GetPublicKeyFingerprint(metadata.KeyName);
        if (lookup.Status == NetworkIdentityKeyLookupStatus.Missing)
        {
            return NetworkIdentityResolution.KeyMissing(
                filePath,
                metadata,
                lookup.ErrorMessage ?? "Network Identity CNG key is missing.");
        }

        if (lookup.Status == NetworkIdentityKeyLookupStatus.Invalid)
        {
            return NetworkIdentityResolution.Invalid(
                filePath,
                lookup.ErrorMessage ?? "Network Identity CNG key is unusable.",
                metadata);
        }

        if (!string.Equals(
            metadata.PublicKeyFingerprint,
            lookup.PublicKeyFingerprint,
            StringComparison.Ordinal))
        {
            return NetworkIdentityResolution.Invalid(
                filePath,
                "network identity publicKeyFingerprint does not match the CNG public key",
                metadata);
        }

        return NetworkIdentityResolution.ReadyResult(
            metadata,
            filePath,
            created: false);
    }

    private async Task<NetworkIdentityResolution> CreateNewIdentityAsync(
        InstallationIdentity installationIdentity,
        NetworkIdentityKeyDescriptor descriptor,
        string filePath,
        CancellationToken cancellationToken)
    {
        var creation = _keyStore.Create(descriptor.KeyName);
        if (!creation.Created)
        {
            return NetworkIdentityResolution.Invalid(
                filePath,
                creation.ErrorMessage ?? "Network Identity CNG key could not be created.");
        }

        var metadata = NetworkIdentityMetadata.Create(
            Guid.NewGuid(),
            installationIdentity.InstallationId,
            descriptor,
            creation.PublicKeyFingerprint ?? string.Empty,
            _clock.UtcNow);

        try
        {
            await _store.WriteNewAsync(metadata, cancellationToken);

            return NetworkIdentityResolution.ReadyResult(
                metadata,
                filePath,
                created: true);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            _keyStore.Delete(descriptor.KeyName);

            return NetworkIdentityResolution.Invalid(
                filePath,
                $"network-identity.json could not be written: {exception.Message}");
        }
    }
}
