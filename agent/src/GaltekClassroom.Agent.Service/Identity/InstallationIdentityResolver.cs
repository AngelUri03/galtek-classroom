using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Identity;

public enum InstallationIdentityResolutionStatus
{
    Ready,
    Invalid
}

public sealed record InstallationIdentityResolution(
    InstallationIdentityResolutionStatus Status,
    InstallationIdentity? Identity,
    string FilePath,
    string? ErrorMessage,
    bool Created)
{
    public static InstallationIdentityResolution Ready(
        InstallationIdentity identity,
        string filePath,
        bool created)
    {
        return new InstallationIdentityResolution(
            InstallationIdentityResolutionStatus.Ready,
            identity,
            filePath,
            null,
            created);
    }

    public static InstallationIdentityResolution Invalid(string filePath, string errorMessage)
    {
        return new InstallationIdentityResolution(
            InstallationIdentityResolutionStatus.Invalid,
            null,
            filePath,
            errorMessage,
            Created: false);
    }
}

public sealed class InstallationIdentityResolver
{
    private readonly InstallationIdentityStore _store;
    private readonly IHardwareFingerprintProvider _hardwareFingerprintProvider;
    private readonly ISystemClock _clock;

    public InstallationIdentityResolver(
        InstallationIdentityStore store,
        IHardwareFingerprintProvider hardwareFingerprintProvider,
        ISystemClock clock)
    {
        _store = store;
        _hardwareFingerprintProvider = hardwareFingerprintProvider;
        _clock = clock;
    }

    public async Task<InstallationIdentityResolution> ResolveAsync(CancellationToken cancellationToken)
    {
        var readResult = await _store.ReadAsync(cancellationToken);

        if (readResult.Status == InstallationIdentityStoreReadStatus.Loaded)
        {
            return InstallationIdentityResolution.Ready(
                readResult.Identity!,
                readResult.FilePath,
                created: false);
        }

        if (readResult.Status == InstallationIdentityStoreReadStatus.Invalid)
        {
            return InstallationIdentityResolution.Invalid(
                readResult.FilePath,
                readResult.ErrorMessage ?? "installation.json is invalid");
        }

        var fingerprint = await _hardwareFingerprintProvider.GetCurrentAsync(cancellationToken);
        var identity = InstallationIdentity.Create(
            Guid.NewGuid(),
            fingerprint,
            _clock.UtcNow);

        await _store.WriteNewAsync(identity, cancellationToken);

        return InstallationIdentityResolution.Ready(
            identity,
            readResult.FilePath,
            created: true);
    }
}
