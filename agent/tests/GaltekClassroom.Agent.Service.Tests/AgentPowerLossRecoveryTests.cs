using System.Security.Cryptography;
using System.Text;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.Runtime;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class AgentPowerLossRecoveryTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.PowerLoss.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunMarker_WhenStoppedCleanly_NextStartupReportsClean()
    {
        var first = CreateRunMarker();

        var firstStart = await first.MarkStartedAsync(CancellationToken.None);
        var stop = await first.MarkStoppedAsync(CancellationToken.None);
        var secondStart = await CreateRunMarker().MarkStartedAsync(CancellationToken.None);

        Assert.False(firstStart.PreviousShutdownWasUnclean);
        Assert.True(firstStart.MarkerWritten);
        Assert.True(stop.Removed);
        Assert.False(secondStart.PreviousShutdownWasUnclean);
    }

    [Fact]
    public async Task RunMarker_WhenPreviousMarkerRemains_ReportsUncleanShutdown()
    {
        await CreateRunMarker().MarkStartedAsync(CancellationToken.None);

        var restart = await CreateRunMarker().MarkStartedAsync(CancellationToken.None);

        Assert.True(restart.PreviousShutdownWasUnclean);
        Assert.True(restart.MarkerWritten);
    }

    [Fact]
    public async Task RunMarker_DoesNotWritePeriodically()
    {
        var marker = CreateRunMarker();

        await marker.MarkStartedAsync(CancellationToken.None);
        var before = await File.ReadAllTextAsync(marker.FilePath);
        _ = marker.FilePath;
        var after = await File.ReadAllTextAsync(marker.FilePath);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task UncleanShutdownMarker_DoesNotRegenerateNetworkIdentity()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        var installation = CreateInstallationIdentity();
        var firstIdentity = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);
        await CreateRunMarker().MarkStartedAsync(CancellationToken.None);

        var restartMarker = await CreateRunMarker().MarkStartedAsync(CancellationToken.None);
        var secondIdentity = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);

        Assert.True(restartMarker.PreviousShutdownWasUnclean);
        Assert.Equal(NetworkIdentityStatus.Ready, secondIdentity.Status);
        Assert.False(secondIdentity.Created);
        Assert.Equal(firstIdentity.Metadata!.NetworkIdentityId, secondIdentity.Metadata!.NetworkIdentityId);
        Assert.Equal(firstIdentity.Metadata.PublicKeyFingerprint, secondIdentity.Metadata.PublicKeyFingerprint);
        Assert.Equal(1, keyStore.CreateCalls);
    }

    [Fact]
    public void RuntimeState_WhenOnlyLocalIdentityIsReady_ReachesMinimalReadyWithoutNetwork()
    {
        var runtimeState = new AgentRuntimeState();

        runtimeState.ObservePreviousShutdown(previousShutdownWasUnclean: false);
        runtimeState.SetInstallationIdentity(CreateInstallationIdentity());

        Assert.Equal(AgentStartupPhase.MinimalReady, runtimeState.Snapshot.StartupPhase);
        Assert.False(runtimeState.Snapshot.RecoveryActive);
    }

    [Fact]
    public async Task RuntimeState_WhenOnlyLocalIdentityIsReady_DoesNotCompleteNetworkReadiness()
    {
        var runtimeState = new AgentRuntimeState();
        using var cancellation = new CancellationTokenSource();

        runtimeState.ObservePreviousShutdown(previousShutdownWasUnclean: false);
        runtimeState.SetInstallationIdentity(CreateInstallationIdentity());
        var waitForNetwork = runtimeState.WaitForNetworkIdentityAsync(cancellation.Token);

        await Task.Yield();
        Assert.False(waitForNetwork.IsCompleted);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waitForNetwork);
    }

    [Fact]
    public async Task RuntimeState_WaitForNetworkIdentity_CompletesWhenSecurityReady()
    {
        var runtimeState = new AgentRuntimeState();
        var networkIdentity = CreateNetworkIdentity();
        var waitForNetwork = runtimeState.WaitForNetworkIdentityAsync(CancellationToken.None);

        runtimeState.SetNetworkIdentity(networkIdentity);

        Assert.Same(networkIdentity, await waitForNetwork);
        Assert.Equal(AgentStartupPhase.SecurityReady, runtimeState.Snapshot.StartupPhase);
    }

    [Fact]
    public void RuntimeState_OperationReadyDoesNotSkipNetworkReady()
    {
        var runtimeState = new AgentRuntimeState();

        runtimeState.ObservePreviousShutdown(previousShutdownWasUnclean: false);
        runtimeState.SetInstallationIdentity(CreateInstallationIdentity());
        runtimeState.MarkOperationReady();

        Assert.Equal(AgentStartupPhase.MinimalReady, runtimeState.Snapshot.StartupPhase);

        runtimeState.SetNetworkIdentity(CreateNetworkIdentity());
        Assert.Equal(AgentStartupPhase.SecurityReady, runtimeState.Snapshot.StartupPhase);

        runtimeState.MarkNetworkReady();
        Assert.Equal(AgentStartupPhase.OperationReady, runtimeState.Snapshot.StartupPhase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private ServiceRunMarker CreateRunMarker()
    {
        return new ServiceRunMarker(
            new ServiceRunMarkerOptions(_dataDirectory, ServiceRunMarker.DefaultFileName),
            new FakeClock(FixedNowUtc));
    }

    private NetworkIdentityResolver CreateResolver(FakeNetworkIdentityKeyStore keyStore)
    {
        return new NetworkIdentityResolver(
            new NetworkIdentityStore(
                new NetworkIdentityStoreOptions(_dataDirectory),
                new NoOpNetworkIdentityFileSecurity()),
            keyStore,
            new FakeClock(FixedNowUtc));
    }

    private static InstallationIdentity CreateInstallationIdentity()
    {
        return InstallationIdentity.Create(
            InstallationId,
            HardwareFingerprintFactory.FromRawValues(
                ["CPU SERIAL 1"],
                ["MOTHERBOARD SERIAL 1"],
                ["AA11BB22CC33"],
                ["DISK SERIAL 1"]),
            FixedNowUtc);
    }

    private static NetworkIdentityMetadata CreateNetworkIdentity()
    {
        var descriptor = NetworkIdentityKeyDescriptor.ForInstallation(InstallationId);

        return NetworkIdentityMetadata.Create(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            InstallationId,
            descriptor,
            Sha256Hex("PUBLIC:network"),
            FixedNowUtc);
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed class FakeClock : ISystemClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeNetworkIdentityKeyStore : INetworkIdentityKeyStore
    {
        private readonly Dictionary<string, string> _publicFingerprints = new(StringComparer.Ordinal);

        public int CreateCalls { get; private set; }

        public bool Exists(string keyName)
        {
            return _publicFingerprints.ContainsKey(keyName);
        }

        public NetworkIdentityKeyCreationResult Create(string keyName)
        {
            CreateCalls++;

            if (_publicFingerprints.ContainsKey(keyName))
            {
                return NetworkIdentityKeyCreationResult.AlreadyExists(keyName);
            }

            _publicFingerprints[keyName] = Sha256Hex($"PUBLIC:{keyName}");
            return NetworkIdentityKeyCreationResult.Success(_publicFingerprints[keyName]);
        }

        public NetworkIdentityKeyLookupResult GetPublicKeyFingerprint(string keyName)
        {
            return _publicFingerprints.TryGetValue(keyName, out var fingerprint)
                ? NetworkIdentityKeyLookupResult.Found(fingerprint)
                : NetworkIdentityKeyLookupResult.Missing(keyName);
        }

        public NetworkIdentityPublicKeyResult GetPublicKey(string keyName)
        {
            return _publicFingerprints.TryGetValue(keyName, out var fingerprint)
                ? NetworkIdentityPublicKeyResult.Found(
                    fingerprint,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"PUBLIC:{keyName}")))
                : NetworkIdentityPublicKeyResult.Missing(keyName);
        }

        public NetworkIdentitySignatureResult Sign(string keyName, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);

            return _publicFingerprints.ContainsKey(keyName)
                ? NetworkIdentitySignatureResult.Success(Convert.ToBase64String(SHA256.HashData(data)))
                : NetworkIdentitySignatureResult.Missing(keyName);
        }

        public NetworkIdentityCertificateResult CreateSelfSignedCertificate(
            string keyName,
            string subjectName,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter)
        {
            return _publicFingerprints.ContainsKey(keyName)
                ? NetworkIdentityCertificateResult.Invalid("Fake key store does not create certificates.")
                : NetworkIdentityCertificateResult.Missing(keyName);
        }

        public NetworkIdentityKeyDeleteResult Delete(string keyName)
        {
            _publicFingerprints.Remove(keyName);
            return NetworkIdentityKeyDeleteResult.Success();
        }
    }
}
