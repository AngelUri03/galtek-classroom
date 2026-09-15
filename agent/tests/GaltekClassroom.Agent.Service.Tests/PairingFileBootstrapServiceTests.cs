using System.Security.Cryptography;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.Pairing;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class PairingFileBootstrapServiceTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.PairingFileBootstrap.Tests",
        Guid.NewGuid().ToString("N"));

    private readonly RsaNetworkIdentityKeyStore _clientKeys = new();
    private readonly RsaNetworkIdentityKeyStore _masterKeys = new();
    private readonly MutableClock _clock = new(FixedNow);

    [Fact]
    public async Task ExportClientDescriptorAsync_WritesOnlyPublicPairingDescriptor()
    {
        var service = CreateService();
        var descriptorPath = Path.Combine(_dataDirectory, "client-descriptor.json");

        var result = await service.ExportClientDescriptorAsync(descriptorPath, CancellationToken.None);
        var json = await File.ReadAllTextAsync(descriptorPath);
        using var document = JsonDocument.Parse(json);

        Assert.True(result.Succeeded);
        Assert.Equal("PAIRING_EXPORT_DESCRIPTOR", result.Operation);
        Assert.Equal(PairingConstants.ClientDescriptorPurpose, document.RootElement.GetProperty("purpose").GetString());
        Assert.True(document.RootElement.TryGetProperty("publicKeySubjectPublicKeyInfoBase64", out _));
        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptChallengeAsync_WithValidChallengeAndApproval_WritesResponseAndTrust()
    {
        var clientIdentity = await ResolveClientIdentityAsync();
        var challengePath = Path.Combine(_dataDirectory, "challenge.json");
        var responsePath = Path.Combine(_dataDirectory, "response.json");
        await WriteJsonAsync(challengePath, CreateChallenge(clientIdentity, FixedNow));

        var result = await CreateService().AcceptChallengeAsync(
            challengePath,
            responsePath,
            explicitApproval: true,
            CancellationToken.None);
        var responseJson = await File.ReadAllTextAsync(responsePath);

        Assert.True(result.Succeeded);
        Assert.Equal("PAIRING_ACCEPT_CHALLENGE", result.Operation);
        Assert.Equal(PairingStatus.Paired.ToCode(), result.Status);
        Assert.DoesNotContain("private", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", responseJson, StringComparison.OrdinalIgnoreCase);

        var trust = await new ClientTrustStore(
                new ClientTrustStoreOptions(_dataDirectory),
                new NoOpNetworkIdentityFileSecurity())
            .ReadAsync(CancellationToken.None);
        Assert.Equal(ClientTrustStoreReadStatus.Loaded, trust.Status);
        Assert.Equal(PairingStatus.Paired.ToCode(), Assert.Single(trust.Document!.AuthorizedMasters).Status);
    }

    [Fact]
    public async Task AcceptChallengeAsync_WithoutExplicitApproval_RejectsAndDoesNotWriteResponse()
    {
        var clientIdentity = await ResolveClientIdentityAsync();
        var challengePath = Path.Combine(_dataDirectory, "challenge.json");
        var responsePath = Path.Combine(_dataDirectory, "response.json");
        await WriteJsonAsync(challengePath, CreateChallenge(clientIdentity, FixedNow));

        var result = await CreateService().AcceptChallengeAsync(
            challengePath,
            responsePath,
            explicitApproval: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PairingConstants.ExplicitApprovalRequiredErrorCode, result.ErrorCode);
        Assert.False(File.Exists(responsePath));
    }

    [Fact]
    public async Task AcceptChallengeAsync_WithExpiredChallenge_DelegatesRejectionToPairingService()
    {
        var clientIdentity = await ResolveClientIdentityAsync();
        var challengePath = Path.Combine(_dataDirectory, "challenge.json");
        var responsePath = Path.Combine(_dataDirectory, "response.json");
        await WriteJsonAsync(challengePath, CreateChallenge(clientIdentity, FixedNow.AddMinutes(-10)));

        var result = await CreateService().AcceptChallengeAsync(
            challengePath,
            responsePath,
            explicitApproval: true,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PairingConstants.ChallengeExpiredErrorCode, result.ErrorCode);
        Assert.False(File.Exists(responsePath));
    }

    public void Dispose()
    {
        _clientKeys.Dispose();
        _masterKeys.Dispose();

        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private async Task<NetworkIdentityMetadata> ResolveClientIdentityAsync()
    {
        var installation = await CreateInstallationResolver().ResolveAsync(CancellationToken.None);
        var network = await CreateNetworkResolver().ResolveAsync(
            installation.Identity!,
            CancellationToken.None);
        return network.Metadata!;
    }

    private PairingFileBootstrapService CreateService()
    {
        var trustStore = new ClientTrustStore(
            new ClientTrustStoreOptions(_dataDirectory),
            new NoOpNetworkIdentityFileSecurity());
        var pairingService = new ClientPairingService(trustStore, _clientKeys, _clock);

        return new PairingFileBootstrapService(
            CreateInstallationResolver(),
            CreateNetworkResolver(),
            _clientKeys,
            pairingService,
            _clock);
    }

    private InstallationIdentityResolver CreateInstallationResolver()
    {
        return new InstallationIdentityResolver(
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            new StaticHardwareFingerprintProvider(),
            _clock);
    }

    private NetworkIdentityResolver CreateNetworkResolver()
    {
        return new NetworkIdentityResolver(
            new NetworkIdentityStore(
                new NetworkIdentityStoreOptions(_dataDirectory),
                new NoOpNetworkIdentityFileSecurity()),
            _clientKeys,
            _clock);
    }

    private PairingChallenge CreateChallenge(
        NetworkIdentityMetadata clientIdentity,
        DateTimeOffset issuedAtUtc)
    {
        const string masterKeyName = "master-key";
        if (!_masterKeys.Exists(masterKeyName))
        {
            _masterKeys.Create(masterKeyName);
        }

        var masterPublicKey = _masterKeys.GetPublicKey(masterKeyName);
        var clientPublicKey = _clientKeys.GetPublicKey(clientIdentity.KeyName);
        var challenge = new PairingChallenge
        {
            ChallengeId = Guid.NewGuid(),
            MasterNetworkIdentityId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ClientNetworkIdentityId = clientIdentity.NetworkIdentityId,
            ClientInstallationId = clientIdentity.InstallationId,
            MasterPublicKeyFingerprint = masterPublicKey.PublicKeyFingerprint!,
            ClientPublicKeyFingerprint = clientIdentity.PublicKeyFingerprint,
            MasterPublicKeySubjectPublicKeyInfoBase64 = masterPublicKey.SubjectPublicKeyInfoBase64!,
            ClientPublicKeySubjectPublicKeyInfoBase64 = clientPublicKey.SubjectPublicKeyInfoBase64!,
            NonceBase64 = PairingCrypto.CreateNonceBase64(),
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = issuedAtUtc.AddMinutes(PairingConstants.PairingChallengeTtlMinutes)
        };

        return challenge with
        {
            MasterSignatureBase64 = _masterKeys.Sign(
                masterKeyName,
                PairingCrypto.CanonicalChallengeBytes(challenge)).SignatureBase64!
        };
    }

    private static async Task WriteJsonAsync(string path, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
    }

    private sealed class MutableClock : ISystemClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class StaticHardwareFingerprintProvider : IHardwareFingerprintProvider
    {
        public Task<HardwareFingerprint> GetCurrentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new HardwareFingerprint(
                "0".PadLeft(64, 'a'),
                "0".PadLeft(64, 'b'),
                "0".PadLeft(64, 'c'),
                "0".PadLeft(64, 'd')));
        }
    }

    private sealed class RsaNetworkIdentityKeyStore : INetworkIdentityKeyStore, IDisposable
    {
        private readonly Dictionary<string, RSA> _keys = new(StringComparer.Ordinal);

        public bool Exists(string keyName)
        {
            return _keys.ContainsKey(keyName);
        }

        public NetworkIdentityKeyCreationResult Create(string keyName)
        {
            if (_keys.ContainsKey(keyName))
            {
                return NetworkIdentityKeyCreationResult.AlreadyExists(keyName);
            }

            _keys[keyName] = RSA.Create(2048);
            return NetworkIdentityKeyCreationResult.Success(Fingerprint(_keys[keyName]));
        }

        public NetworkIdentityKeyLookupResult GetPublicKeyFingerprint(string keyName)
        {
            return _keys.TryGetValue(keyName, out var key)
                ? NetworkIdentityKeyLookupResult.Found(Fingerprint(key))
                : NetworkIdentityKeyLookupResult.Missing(keyName);
        }

        public NetworkIdentityPublicKeyResult GetPublicKey(string keyName)
        {
            return _keys.TryGetValue(keyName, out var key)
                ? NetworkIdentityPublicKeyResult.Found(
                    Fingerprint(key),
                    Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))
                : NetworkIdentityPublicKeyResult.Missing(keyName);
        }

        public NetworkIdentitySignatureResult Sign(string keyName, byte[] data)
        {
            if (!_keys.TryGetValue(keyName, out var key))
            {
                return NetworkIdentitySignatureResult.Missing(keyName);
            }

            var signature = key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return NetworkIdentitySignatureResult.Success(Convert.ToBase64String(signature));
        }

        public NetworkIdentityCertificateResult CreateSelfSignedCertificate(
            string keyName,
            string subjectName,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter)
        {
            return NetworkIdentityCertificateResult.Invalid("not needed in this test");
        }

        public NetworkIdentityKeyDeleteResult Delete(string keyName)
        {
            if (!_keys.Remove(keyName, out var key))
            {
                return NetworkIdentityKeyDeleteResult.Missing();
            }

            key.Dispose();
            return NetworkIdentityKeyDeleteResult.Success();
        }

        public void Dispose()
        {
            foreach (var key in _keys.Values)
            {
                key.Dispose();
            }

            _keys.Clear();
        }

        private static string Fingerprint(RSA key)
        {
            return Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        }
    }
}
