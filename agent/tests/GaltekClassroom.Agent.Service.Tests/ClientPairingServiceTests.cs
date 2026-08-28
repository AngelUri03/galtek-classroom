using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.Pairing;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class ClientPairingServiceTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ClientNetworkIdentityId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly Guid MasterNetworkIdentityId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000");
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 27, 15, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.ClientPairing.Tests",
        Guid.NewGuid().ToString("N"));

    private readonly RsaNetworkIdentityKeyStore _clientKeys = new();
    private readonly RsaNetworkIdentityKeyStore _masterKeys = new();
    private readonly MutableClock _clock = new(FixedNow);

    [Fact]
    public async Task AcceptChallengeAsync_WithValidChallenge_PersistsAuthorizedMasterAndSignedResponse()
    {
        var clientIdentity = CreateClientNetworkIdentity();
        const string masterKeyName = "master-key-a";
        var challenge = CreateChallenge(masterKeyName, MasterNetworkIdentityId, clientIdentity);
        var service = CreateService();

        var result = await service.AcceptChallengeAsync(
            challenge,
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Response);
        Assert.Equal(PairingStatus.Paired, result.Status);
        Assert.True(PairingCrypto.VerifySignature(
            _clientKeys.GetPublicKey(clientIdentity.KeyName).SubjectPublicKeyInfoBase64!,
            PairingCrypto.CanonicalResponseBytes(result.Response!),
            result.Response!.ClientSignatureBase64));

        var document = (await CreateTrustStore().ReadAsync(CancellationToken.None)).Document!;
        var master = Assert.Single(document.AuthorizedMasters);
        Assert.Equal(PairingStatus.Paired.ToCode(), master.Status);
        Assert.Equal(MasterNetworkIdentityId, master.MasterNetworkIdentityId);
        Assert.Equal(ClientNetworkIdentityId, master.ClientNetworkIdentityId);
        Assert.Equal(InstallationId, master.ClientInstallationId);
        Assert.Equal(challenge.MasterPublicKeyFingerprint, master.MasterPublicKeyFingerprint);
        Assert.Equal(clientIdentity.PublicKeyFingerprint, master.ClientPublicKeyFingerprint);
        Assert.NotNull(master.PairedAtUtc);
        Assert.Null(master.RevokedAtUtc);
        Assert.Equal(challenge.ChallengeId, master.PairedChallengeId);
        Assert.Single(document.ConsumedChallenges);

        var authorization = await service.IsMasterAuthorizedAsync(
            MasterDescriptor(masterKeyName, MasterNetworkIdentityId),
            clientIdentity,
            CancellationToken.None);
        Assert.True(authorization.Authorized);
    }

    [Fact]
    public async Task AcceptChallengeAsync_WithInvalidMasterSignature_RejectsTrust()
    {
        var clientIdentity = CreateClientNetworkIdentity();
        var challenge = CreateChallenge("master-key-a", MasterNetworkIdentityId, clientIdentity) with
        {
            MasterSignatureBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(256))
        };

        var result = await CreateService().AcceptChallengeAsync(
            challenge,
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(PairingConstants.SignatureInvalidErrorCode, result.ErrorCode);
        Assert.Equal(ClientTrustStoreReadStatus.Missing, (await CreateTrustStore().ReadAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task AcceptChallengeAsync_WithExpiredChallenge_RejectsTrust()
    {
        var clientIdentity = CreateClientNetworkIdentity();
        var challenge = CreateChallenge(
            "master-key-a",
            MasterNetworkIdentityId,
            clientIdentity,
            issuedAtUtc: FixedNow.AddMinutes(-10));
        _clock.UtcNow = FixedNow.AddMinutes(1);

        var result = await CreateService().AcceptChallengeAsync(
            challenge,
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(PairingConstants.ChallengeExpiredErrorCode, result.ErrorCode);
        Assert.Equal(ClientTrustStoreReadStatus.Missing, (await CreateTrustStore().ReadAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task AcceptChallengeAsync_WhenChallengeIsReplayed_RejectsSecondUse()
    {
        var clientIdentity = CreateClientNetworkIdentity();
        var challenge = CreateChallenge("master-key-a", MasterNetworkIdentityId, clientIdentity);
        var service = CreateService();

        var first = await service.AcceptChallengeAsync(
            challenge,
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);
        var replay = await service.AcceptChallengeAsync(
            challenge,
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.False(replay.Accepted);
        Assert.Equal(PairingConstants.ReplayRejectedErrorCode, replay.ErrorCode);
        Assert.Single((await CreateTrustStore().ReadAsync(CancellationToken.None)).Document!.ConsumedChallenges);
    }

    [Fact]
    public async Task AcceptChallengeAsync_WithIncorrectFingerprint_RejectsTrust()
    {
        var clientIdentity = CreateClientNetworkIdentity();
        var challenge = CreateChallenge("master-key-a", MasterNetworkIdentityId, clientIdentity) with
        {
            MasterPublicKeyFingerprint = new string('0', 64)
        };

        var result = await CreateService().AcceptChallengeAsync(
            challenge,
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(PairingConstants.FingerprintMismatchErrorCode, result.ErrorCode);
        Assert.Equal(ClientTrustStoreReadStatus.Missing, (await CreateTrustStore().ReadAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task IsMasterAuthorizedAsync_WhenTrustStoreReopens_PreservesPairedMaster()
    {
        var clientIdentity = CreateClientNetworkIdentity();
        var challenge = CreateChallenge("master-key-a", MasterNetworkIdentityId, clientIdentity);
        await CreateService().AcceptChallengeAsync(
            challenge,
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);

        var reopened = CreateService();
        var authorization = await reopened.IsMasterAuthorizedAsync(
            MasterDescriptor("master-key-a", MasterNetworkIdentityId),
            clientIdentity,
            CancellationToken.None);

        Assert.True(authorization.Authorized);
    }

    [Fact]
    public async Task RevokeMasterAsync_BlocksFutureAuthorizationWithoutDeletingTrustMetadata()
    {
        var clientIdentity = CreateClientNetworkIdentity();
        await CreateService().AcceptChallengeAsync(
            CreateChallenge("master-key-a", MasterNetworkIdentityId, clientIdentity),
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);

        var service = CreateService();
        var revoked = await service.RevokeMasterAsync(
            MasterNetworkIdentityId,
            clientIdentity,
            CancellationToken.None);
        var authorization = await service.IsMasterAuthorizedAsync(
            MasterDescriptor("master-key-a", MasterNetworkIdentityId),
            clientIdentity,
            CancellationToken.None);

        Assert.True(revoked);
        Assert.False(authorization.Authorized);
        Assert.Equal(PairingStatus.Revoked, authorization.Status);

        var record = Assert.Single((await CreateTrustStore().ReadAsync(CancellationToken.None)).Document!.AuthorizedMasters);
        Assert.Equal(PairingStatus.Revoked.ToCode(), record.Status);
        Assert.Equal(ClientNetworkIdentityId, record.ClientNetworkIdentityId);
        Assert.Equal(InstallationId, record.ClientInstallationId);
        Assert.NotNull(record.RevokedAtUtc);
    }

    [Fact]
    public async Task IsMasterAuthorizedAsync_WhenMasterIsUnpaired_FailsClosed()
    {
        var clientIdentity = CreateClientNetworkIdentity();

        var authorization = await CreateService().IsMasterAuthorizedAsync(
            MasterDescriptor("master-key-a", MasterNetworkIdentityId),
            clientIdentity,
            CancellationToken.None);

        Assert.False(authorization.Authorized);
        Assert.Equal(PairingStatus.Unpaired, authorization.Status);
        Assert.Equal(ClassroomOperationErrorCodes.MasterNotPaired, authorization.ErrorCode);
    }

    [Fact]
    public async Task ClientTrustSupportsMultipleMasters()
    {
        var clientIdentity = CreateClientNetworkIdentity();
        var secondMasterId = Guid.Parse("dddddddd-eeee-ffff-0000-111111111111");
        var service = CreateService();

        await service.AcceptChallengeAsync(
            CreateChallenge("master-key-a", MasterNetworkIdentityId, clientIdentity),
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);
        await service.AcceptChallengeAsync(
            CreateChallenge("master-key-b", secondMasterId, clientIdentity),
            CreateInstallationIdentity(),
            clientIdentity,
            explicitApproval: true,
            CancellationToken.None);

        var first = await service.IsMasterAuthorizedAsync(
            MasterDescriptor("master-key-a", MasterNetworkIdentityId),
            clientIdentity,
            CancellationToken.None);
        var second = await service.IsMasterAuthorizedAsync(
            MasterDescriptor("master-key-b", secondMasterId),
            clientIdentity,
            CancellationToken.None);

        Assert.True(first.Authorized);
        Assert.True(second.Authorized);
        Assert.Equal(2, (await CreateTrustStore().ReadAsync(CancellationToken.None)).Document!.AuthorizedMasters.Count);
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

    private ClientPairingService CreateService()
    {
        return new ClientPairingService(CreateTrustStore(), _clientKeys, _clock);
    }

    private ClientTrustStore CreateTrustStore()
    {
        return new ClientTrustStore(
            new ClientTrustStoreOptions(_dataDirectory),
            new NoOpNetworkIdentityFileSecurity());
    }

    private NetworkIdentityMetadata CreateClientNetworkIdentity()
    {
        var descriptor = NetworkIdentityKeyDescriptor.ForInstallation(InstallationId);
        if (!_clientKeys.Exists(descriptor.KeyName))
        {
            _clientKeys.Create(descriptor.KeyName);
        }

        var publicKey = _clientKeys.GetPublicKey(descriptor.KeyName);

        return NetworkIdentityMetadata.Create(
            ClientNetworkIdentityId,
            InstallationId,
            descriptor,
            publicKey.PublicKeyFingerprint!,
            FixedNow);
    }

    private PairingChallenge CreateChallenge(
        string masterKeyName,
        Guid masterNetworkIdentityId,
        NetworkIdentityMetadata clientIdentity,
        DateTimeOffset? issuedAtUtc = null)
    {
        if (!_masterKeys.Exists(masterKeyName))
        {
            _masterKeys.Create(masterKeyName);
        }

        var masterPublicKey = _masterKeys.GetPublicKey(masterKeyName);
        var clientPublicKey = _clientKeys.GetPublicKey(clientIdentity.KeyName);
        var issuedAt = (issuedAtUtc ?? FixedNow).ToUniversalTime();
        var challenge = new PairingChallenge
        {
            SchemaVersion = PairingConstants.SchemaVersion,
            Purpose = PairingConstants.Purpose,
            ChallengeId = Guid.NewGuid(),
            MasterNetworkIdentityId = masterNetworkIdentityId,
            ClientNetworkIdentityId = clientIdentity.NetworkIdentityId,
            ClientInstallationId = clientIdentity.InstallationId,
            MasterPublicKeyFingerprint = masterPublicKey.PublicKeyFingerprint!,
            ClientPublicKeyFingerprint = clientIdentity.PublicKeyFingerprint,
            MasterPublicKeySubjectPublicKeyInfoBase64 = masterPublicKey.SubjectPublicKeyInfoBase64!,
            ClientPublicKeySubjectPublicKeyInfoBase64 = clientPublicKey.SubjectPublicKeyInfoBase64!,
            NonceBase64 = PairingCrypto.CreateNonceBase64(),
            IssuedAtUtc = issuedAt,
            ExpiresAtUtc = issuedAt.AddMinutes(PairingConstants.PairingChallengeTtlMinutes)
        };

        var signature = _masterKeys.Sign(masterKeyName, PairingCrypto.CanonicalChallengeBytes(challenge));

        return challenge with { MasterSignatureBase64 = signature.SignatureBase64! };
    }

    private MasterNetworkIdentityDescriptor MasterDescriptor(
        string masterKeyName,
        Guid masterNetworkIdentityId)
    {
        if (!_masterKeys.Exists(masterKeyName))
        {
            _masterKeys.Create(masterKeyName);
        }

        var publicKey = _masterKeys.GetPublicKey(masterKeyName);

        return new MasterNetworkIdentityDescriptor(
            masterNetworkIdentityId,
            publicKey.PublicKeyFingerprint!,
            publicKey.SubjectPublicKeyInfoBase64!);
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
            FixedNow);
    }

    private sealed class MutableClock : ISystemClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
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
            ArgumentNullException.ThrowIfNull(data);

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
            if (!_keys.TryGetValue(keyName, out var key))
            {
                return NetworkIdentityCertificateResult.Missing(keyName);
            }

            var request = new CertificateRequest(
                $"CN={subjectName}",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new Oid("1.3.6.1.5.5.7.3.1"),
                    new Oid("1.3.6.1.5.5.7.3.2")
                },
                critical: false));

            return NetworkIdentityCertificateResult.Success(
                Fingerprint(key),
                request.CreateSelfSigned(notBefore, notAfter));
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
