using System.Security.Cryptography;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.Pairing;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class HostnameRenameSecurityStoreTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ClientNetworkIdentityId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid MasterNetworkIdentityId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private const string LicenseToken = "galtek-commercial-license-token-fixture";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.HostnameRenameSecurityStores.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RenameIch11ToPc14_PreservesNetworkIdentityAuthorizedMastersAndLicenseBytes()
    {
        var networkStore = CreateNetworkStore();
        var trustStore = CreateTrustStore();
        var licenseStore = new CommercialLicenseStore(new CommercialLicenseStoreOptions(_dataDirectory));
        NetworkIdentityMetadata networkIdentity = CreateNetworkIdentity();
        ClientTrustDocument trust = CreateTrustDocument(networkIdentity);

        await networkStore.WriteNewAsync(networkIdentity, CancellationToken.None);
        await trustStore.SaveAsync(trust, CancellationToken.None);
        await licenseStore.WriteAsync(LicenseToken, CancellationToken.None);

        SecurityStoreSnapshot beforeRename = await CaptureAsync("ICH11");
        SecurityStoreSnapshot afterRename = await CaptureAsync("PC14");

        Assert.Equal("ICH11", beforeRename.CurrentHostname);
        Assert.Equal("PC14", afterRename.CurrentHostname);
        Assert.Equal(beforeRename.NetworkIdentity, afterRename.NetworkIdentity);
        Assert.Equal(beforeRename.NetworkIdentityHash, afterRename.NetworkIdentityHash);
        Assert.Equal(beforeRename.TrustHash, afterRename.TrustHash);
        Assert.Equal(beforeRename.LicenseHash, afterRename.LicenseHash);
        Assert.Equal(LicenseToken, afterRename.LicenseToken);

        AuthorizedMasterTrustRecord master = Assert.Single(afterRename.Trust.AuthorizedMasters);
        Assert.Equal(MasterNetworkIdentityId, master.MasterNetworkIdentityId);
        Assert.Equal(ClientNetworkIdentityId, master.ClientNetworkIdentityId);
        Assert.Equal(InstallationId, master.ClientInstallationId);
        Assert.Equal(PairingStatus.Paired.ToCode(), master.Status);
        Assert.Equal(networkIdentity.PublicKeyFingerprint, master.ClientPublicKeyFingerprint);

        string persistedText = string.Join(
            '\n',
            await File.ReadAllTextAsync(networkStore.FilePath),
            await File.ReadAllTextAsync(trustStore.FilePath),
            await File.ReadAllTextAsync(licenseStore.FilePath));
        Assert.DoesNotContain("ICH11", persistedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PC14", persistedText, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SecurityStoreSnapshot> CaptureAsync(string currentHostname)
    {
        var networkStore = CreateNetworkStore();
        var trustStore = CreateTrustStore();
        var licenseStore = new CommercialLicenseStore(new CommercialLicenseStoreOptions(_dataDirectory));
        NetworkIdentityStoreReadResult network = await networkStore.ReadAsync(CancellationToken.None);
        ClientTrustStoreReadResult trust = await trustStore.ReadAsync(CancellationToken.None);
        CommercialLicenseStoreReadResult license = await licenseStore.ReadAsync(CancellationToken.None);

        Assert.Equal(NetworkIdentityStoreReadStatus.Loaded, network.Status);
        Assert.Equal(ClientTrustStoreReadStatus.Loaded, trust.Status);
        Assert.Equal(CommercialLicenseStoreReadStatus.Loaded, license.Status);

        return new SecurityStoreSnapshot(
            currentHostname,
            network.Metadata!,
            trust.Document!,
            license.Token!,
            await HashFileAsync(networkStore.FilePath),
            await HashFileAsync(trustStore.FilePath),
            await HashFileAsync(licenseStore.FilePath));
    }

    private NetworkIdentityStore CreateNetworkStore()
    {
        return new NetworkIdentityStore(
            new NetworkIdentityStoreOptions(_dataDirectory),
            new NoOpNetworkIdentityFileSecurity());
    }

    private ClientTrustStore CreateTrustStore()
    {
        return new ClientTrustStore(
            new ClientTrustStoreOptions(_dataDirectory),
            new NoOpNetworkIdentityFileSecurity());
    }

    private static NetworkIdentityMetadata CreateNetworkIdentity()
    {
        var descriptor = NetworkIdentityKeyDescriptor.ForInstallation(InstallationId);
        return NetworkIdentityMetadata.Create(
            ClientNetworkIdentityId,
            InstallationId,
            descriptor,
            new string('a', 64),
            FixedNow);
    }

    private static ClientTrustDocument CreateTrustDocument(NetworkIdentityMetadata networkIdentity)
    {
        using var masterKey = RSA.Create(2048);
        string masterSpki = Convert.ToBase64String(masterKey.ExportSubjectPublicKeyInfo());
        Assert.True(PairingCrypto.TryComputePublicKeyFingerprint(masterSpki, out string masterFingerprint));

        return new ClientTrustDocument
        {
            SchemaVersion = PairingConstants.SchemaVersion,
            ClientNetworkIdentityId = networkIdentity.NetworkIdentityId,
            ClientInstallationId = networkIdentity.InstallationId,
            ClientPublicKeyFingerprint = networkIdentity.PublicKeyFingerprint,
            AuthorizedMasters =
            [
                new AuthorizedMasterTrustRecord
                {
                    SchemaVersion = PairingConstants.SchemaVersion,
                    Status = PairingStatus.Paired.ToCode(),
                    MasterNetworkIdentityId = MasterNetworkIdentityId,
                    ClientNetworkIdentityId = networkIdentity.NetworkIdentityId,
                    ClientInstallationId = networkIdentity.InstallationId,
                    MasterPublicKeyFingerprint = masterFingerprint,
                    ClientPublicKeyFingerprint = networkIdentity.PublicKeyFingerprint,
                    MasterPublicKeySubjectPublicKeyInfoBase64 = masterSpki,
                    PairedAtUtc = FixedNow,
                    PairedChallengeId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff")
                }
            ],
            ConsumedChallenges = []
        };
    }

    private static async Task<string> HashFileAsync(string path)
    {
        return Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private sealed record SecurityStoreSnapshot(
        string CurrentHostname,
        NetworkIdentityMetadata NetworkIdentity,
        ClientTrustDocument Trust,
        string LicenseToken,
        string NetworkIdentityHash,
        string TrustHash,
        string LicenseHash);
}
