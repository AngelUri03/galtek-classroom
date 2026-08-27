using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class NetworkIdentityTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherInstallationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset FixedCreatedAtUtc = new(2026, 8, 27, 13, 0, 0, TimeSpan.Zero);
    private const string PrivateKeyMarker = "PRIVATE-KEY-DO-NOT-SERIALIZE";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.NetworkIdentity.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ResolveAsync_WhenIdentityDoesNotExist_CreatesKeyAndPersistsPublicMetadata()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        var resolver = CreateResolver(keyStore);

        var result = await resolver.ResolveAsync(CreateInstallationIdentity(InstallationId), CancellationToken.None);

        Assert.Equal(NetworkIdentityStatus.Ready, result.Status);
        Assert.True(result.Created);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Metadata);
        Assert.NotEqual(Guid.Empty, result.Metadata.NetworkIdentityId);
        Assert.Equal(InstallationId, result.Metadata.InstallationId);
        Assert.Equal(NetworkIdentityConstants.SchemaVersion, result.Metadata.SchemaVersion);
        Assert.Equal(NetworkIdentityKeyDescriptor.ForInstallation(InstallationId).KeyId, result.Metadata.KeyId);
        Assert.Equal(NetworkIdentityKeyDescriptor.ForInstallation(InstallationId).KeyName, result.Metadata.KeyName);
        Assert.True(NetworkIdentityValidator.IsValidSha256Hex(result.Metadata.PublicKeyFingerprint));
        Assert.True(keyStore.Exists(result.Metadata.KeyName));

        var metadataJson = await File.ReadAllTextAsync(Path.Combine(
            _dataDirectory,
            NetworkIdentityConstants.FileName));
        Assert.DoesNotContain(PrivateKeyMarker, metadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("privateKey", metadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", metadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_WhenReopened_PreservesNetworkIdentityAndFingerprint()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        var installation = CreateInstallationIdentity(InstallationId);

        var first = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);
        var second = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);

        Assert.Equal(NetworkIdentityStatus.Ready, second.Status);
        Assert.False(second.Created);
        Assert.Equal(first.Metadata!.NetworkIdentityId, second.Metadata!.NetworkIdentityId);
        Assert.Equal(first.Metadata.KeyId, second.Metadata.KeyId);
        Assert.Equal(first.Metadata.KeyName, second.Metadata.KeyName);
        Assert.Equal(first.Metadata.PublicKeyFingerprint, second.Metadata.PublicKeyFingerprint);
        Assert.Equal(1, keyStore.CreateCalls);
    }

    [Fact]
    public async Task ResolveAsync_WhenMetadataIsCorrupt_ReturnsInvalidWithoutRegenerating()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        var installation = CreateInstallationIdentity(InstallationId);
        var first = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);
        var metadataPath = Path.Combine(_dataDirectory, NetworkIdentityConstants.FileName);
        await File.WriteAllTextAsync(metadataPath, "{ not-json");

        var result = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);

        Assert.Equal(NetworkIdentityStatus.Invalid, result.Status);
        Assert.Equal(NetworkIdentityConstants.InvalidErrorCode, result.ErrorCode);
        Assert.Contains("corrupt or incomplete", result.ErrorMessage);
        Assert.Equal(1, keyStore.CreateCalls);
        Assert.True(keyStore.Exists(first.Metadata!.KeyName));
        Assert.Equal("{ not-json", await File.ReadAllTextAsync(metadataPath));
    }

    [Fact]
    public async Task ResolveAsync_WhenKeyIsMissing_ReturnsKeyMissingWithoutRegenerating()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        var installation = CreateInstallationIdentity(InstallationId);
        var first = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);
        keyStore.Delete(first.Metadata!.KeyName);

        var result = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);

        Assert.Equal(NetworkIdentityStatus.KeyMissing, result.Status);
        Assert.Equal(NetworkIdentityConstants.KeyMissingErrorCode, result.ErrorCode);
        Assert.Equal(first.Metadata.NetworkIdentityId, result.Metadata!.NetworkIdentityId);
        Assert.Equal(first.Metadata.PublicKeyFingerprint, result.Metadata.PublicKeyFingerprint);
        Assert.Equal(1, keyStore.CreateCalls);
    }

    [Fact]
    public async Task ResolveAsync_WhenInstallationIdDiffers_ReturnsInstallationMismatch()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        await CreateResolver(keyStore)
            .ResolveAsync(CreateInstallationIdentity(InstallationId), CancellationToken.None);

        var result = await CreateResolver(keyStore)
            .ResolveAsync(CreateInstallationIdentity(OtherInstallationId), CancellationToken.None);

        Assert.Equal(NetworkIdentityStatus.InstallationMismatch, result.Status);
        Assert.Equal(NetworkIdentityConstants.InstallationMismatchErrorCode, result.ErrorCode);
        Assert.Equal(InstallationId, result.Metadata!.InstallationId);
        Assert.Equal(1, keyStore.CreateCalls);
    }

    [Fact]
    public async Task ResolveAsync_WhenFingerprintDoesNotMatchKey_ReturnsInvalid()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        var installation = CreateInstallationIdentity(InstallationId);
        var first = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);
        var tampered = first.Metadata! with
        {
            PublicKeyFingerprint = new string('0', 64)
        };

        await WriteMetadataAsync(tampered);

        var result = await CreateResolver(keyStore).ResolveAsync(installation, CancellationToken.None);

        Assert.Equal(NetworkIdentityStatus.Invalid, result.Status);
        Assert.Equal(NetworkIdentityConstants.InvalidErrorCode, result.ErrorCode);
        Assert.Contains("does not match", result.ErrorMessage);
        Assert.Equal(first.Metadata.NetworkIdentityId, result.Metadata!.NetworkIdentityId);
        Assert.Equal(1, keyStore.CreateCalls);
    }

    [Fact]
    public async Task ResolveAsync_WhenMetadataIsMissingButKeyExists_ReturnsInvalid()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        var descriptor = NetworkIdentityKeyDescriptor.ForInstallation(InstallationId);
        keyStore.Create(descriptor.KeyName);

        var result = await CreateResolver(keyStore)
            .ResolveAsync(CreateInstallationIdentity(InstallationId), CancellationToken.None);

        Assert.Equal(NetworkIdentityStatus.Invalid, result.Status);
        Assert.Equal(NetworkIdentityConstants.InvalidErrorCode, result.ErrorCode);
        Assert.Contains("refusing to regenerate", result.ErrorMessage);
        Assert.Equal(1, keyStore.CreateCalls);
    }

    [Fact]
    public async Task GetStatusAsync_WhenMetadataIsMissing_DoesNotCreateIdentity()
    {
        await WriteInstallationIdentityAsync(CreateInstallationIdentity(InstallationId));
        var keyStore = new FakeNetworkIdentityKeyStore();
        var statusService = CreateStatusService(keyStore);

        var result = await statusService.GetStatusAsync(CancellationToken.None);

        Assert.Equal(NetworkIdentityStatus.NotConfigured, result.Status);
        Assert.Equal(NetworkIdentityConstants.NotConfiguredErrorCode, result.ErrorCode);
        Assert.Equal(0, keyStore.CreateCalls);
        Assert.False(File.Exists(Path.Combine(_dataDirectory, NetworkIdentityConstants.FileName)));
    }

    [Fact]
    public async Task ConsoleStatus_DoesNotExposePrivateKeyMaterial()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        var result = await CreateResolver(keyStore)
            .ResolveAsync(CreateInstallationIdentity(InstallationId), CancellationToken.None);

        var json = NetworkIdentityConsoleJsonSerializer.SerializeStatus(result);

        Assert.Contains("\"status\": \"READY\"", json, StringComparison.Ordinal);
        Assert.Contains(result.Metadata!.NetworkIdentityId.ToString("D"), json, StringComparison.Ordinal);
        Assert.Contains(result.Metadata.PublicKeyFingerprint, json, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateKeyMarker, json, StringComparison.Ordinal);
        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keyName", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConsoleStatus_WhenKeyIsMissing_DoesNotExposeKeyName()
    {
        var keyStore = new FakeNetworkIdentityKeyStore();
        var first = await CreateResolver(keyStore)
            .ResolveAsync(CreateInstallationIdentity(InstallationId), CancellationToken.None);
        keyStore.Delete(first.Metadata!.KeyName);
        var missing = await CreateResolver(keyStore)
            .ResolveAsync(CreateInstallationIdentity(InstallationId), CancellationToken.None);

        var json = NetworkIdentityConsoleJsonSerializer.SerializeStatus(missing);

        Assert.Contains("\"status\": \"KEY_MISSING\"", json, StringComparison.Ordinal);
        Assert.Contains(NetworkIdentityConstants.KeyMissingErrorCode, json, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Metadata.KeyName, json, StringComparison.Ordinal);
        Assert.DoesNotContain("keyName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PrivateKeyMarker, json, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private NetworkIdentityResolver CreateResolver(FakeNetworkIdentityKeyStore keyStore)
    {
        return new NetworkIdentityResolver(
            CreateNetworkIdentityStore(),
            keyStore,
            new FakeClock(FixedCreatedAtUtc));
    }

    private NetworkIdentityStatusService CreateStatusService(FakeNetworkIdentityKeyStore keyStore)
    {
        var resolver = CreateResolver(keyStore);

        return new NetworkIdentityStatusService(
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            CreateNetworkIdentityStore(),
            resolver);
    }

    private NetworkIdentityStore CreateNetworkIdentityStore()
    {
        return new NetworkIdentityStore(
            new NetworkIdentityStoreOptions(_dataDirectory),
            new NoOpNetworkIdentityFileSecurity());
    }

    private async Task WriteMetadataAsync(NetworkIdentityMetadata metadata)
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, NetworkIdentityConstants.FileName),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private async Task WriteInstallationIdentityAsync(InstallationIdentity identity)
    {
        var store = new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory));
        await store.WriteNewAsync(identity, CancellationToken.None);
    }

    private static InstallationIdentity CreateInstallationIdentity(Guid installationId)
    {
        return InstallationIdentity.Create(
            installationId,
            HardwareFingerprintFactory.FromRawValues(
                ["CPU SERIAL 1"],
                ["MOTHERBOARD SERIAL 1"],
                ["AA11BB22CC33"],
                ["DISK SERIAL 1"]),
            FixedCreatedAtUtc);
    }

    private static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexString(hash).ToLowerInvariant();
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

            _publicFingerprints[keyName] = Sha256Hex($"PUBLIC:{keyName}:{PrivateKeyMarker}");

            return NetworkIdentityKeyCreationResult.Success(_publicFingerprints[keyName]);
        }

        public NetworkIdentityKeyLookupResult GetPublicKeyFingerprint(string keyName)
        {
            return _publicFingerprints.TryGetValue(keyName, out var fingerprint)
                ? NetworkIdentityKeyLookupResult.Found(fingerprint)
                : NetworkIdentityKeyLookupResult.Missing(keyName);
        }

        public NetworkIdentityKeyDeleteResult Delete(string keyName)
        {
            _publicFingerprints.Remove(keyName);

            return NetworkIdentityKeyDeleteResult.Success();
        }
    }
}
