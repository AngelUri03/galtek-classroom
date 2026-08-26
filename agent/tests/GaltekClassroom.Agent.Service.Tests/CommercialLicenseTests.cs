using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Shared;
using Microsoft.IdentityModel.Tokens;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class CommercialLicenseTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 8, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.License.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ValidateAsync_WhenRs256TokenIsValid_ReturnsActiveState()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(keyPair, new MutableClock(FixedNowUtc));
        var token = CreateToken(keyPair, identity);

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.Active, result.State.Status);
        Assert.True(result.State.Active);
        Assert.Equal("license-1", result.State.LicenseId);
        Assert.Equal("ESC-00042", result.State.OrganizationId);
        Assert.Equal([CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole], result.State.Roles);
        Assert.True(result.State.Features.ScreenMonitoring);
        Assert.Equal(30, result.State.Features.MaxManagedClients);
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenIsSignedByAnotherPrivateKey_ReturnsTampered()
    {
        using var trustedKeyPair = new TestLicenseKeyPair();
        using var untrustedKeyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(trustedKeyPair, new MutableClock(FixedNowUtc));
        var token = CreateToken(untrustedKeyPair, identity);

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.LicenseTampered, result.State.Status);
        Assert.False(result.State.Active);
    }

    [Fact]
    public async Task ValidateAsync_WhenAlgorithmIsNotRs256_ReturnsTampered()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(keyPair, new MutableClock(FixedNowUtc));
        var symmetricKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var token = CreateToken(
            keyPair,
            identity,
            signingCredentials: new SigningCredentials(symmetricKey, SecurityAlgorithms.HmacSha256));

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.LicenseTampered, result.State.Status);
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenIsExpired_ReturnsLicenseExpired()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(keyPair, new MutableClock(FixedNowUtc));
        var token = CreateToken(
            keyPair,
            identity,
            mutate: payload =>
            {
                payload[JwtRegisteredClaimNames.Iat] = FixedNowUtc.AddDays(-2).ToUnixTimeSeconds();
                payload[JwtRegisteredClaimNames.Exp] = FixedNowUtc.AddSeconds(-1).ToUnixTimeSeconds();
            });

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.LicenseExpired, result.State.Status);
        Assert.False(result.State.Active);
    }

    [Fact]
    public async Task ValidateAsync_WhenProductDoesNotMatch_ReturnsProductMismatch()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(keyPair, new MutableClock(FixedNowUtc));
        var token = CreateToken(
            keyPair,
            identity,
            mutate: payload => payload["product"] = "GALTEK_ONE");

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.ProductMismatch, result.State.Status);
    }

    [Fact]
    public async Task ValidateAsync_WhenSubjectDoesNotMatchInstallation_ReturnsInstallationMismatch()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(keyPair, new MutableClock(FixedNowUtc));
        var token = CreateToken(
            keyPair,
            identity,
            mutate: payload => payload[JwtRegisteredClaimNames.Sub] = Guid.NewGuid().ToString("D"));

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.InstallationMismatch, result.State.Status);
    }

    [Fact]
    public async Task ValidateAsync_WhenHardwareMatchesFourOfFour_ReturnsActive()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(keyPair, new MutableClock(FixedNowUtc));
        var token = CreateToken(keyPair, identity);

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.Active, result.State.Status);
    }

    [Fact]
    public async Task ValidateAsync_WhenHardwareMatchesThreeOfFour_ReturnsActive()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(keyPair, new MutableClock(FixedNowUtc));
        var token = CreateToken(
            keyPair,
            identity,
            mutate: payload => payload["macHash"] = DifferentHash("mac"));

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.Active, result.State.Status);
    }

    [Fact]
    public async Task ValidateAsync_WhenHardwareMatchesTwoOfFour_ReturnsHardwareMismatch()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(keyPair, new MutableClock(FixedNowUtc));
        var token = CreateToken(
            keyPair,
            identity,
            mutate: payload =>
            {
                payload["motherboardHash"] = DifferentHash("motherboard");
                payload["macHash"] = DifferentHash("mac");
            });

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.HardwareMismatch, result.State.Status);
    }

    [Fact]
    public async Task ValidateAsync_WhenSchemaIsUnknown_ReturnsLicenseSchemaUnsupported()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var validator = CreateValidator(keyPair, new MutableClock(FixedNowUtc));
        var token = CreateToken(
            keyPair,
            identity,
            mutate: payload => payload["schemaVersion"] = 99);

        var result = await validator.ValidateAsync(token, identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.LicenseSchemaUnsupported, result.State.Status);
    }

    [Fact]
    public async Task ResolveAsync_WhenLicenseFileDoesNotExist_ReturnsActivationRequired()
    {
        using var keyPair = new TestLicenseKeyPair();
        var manager = CreateManager(_dataDirectory, keyPair, new MutableClock(FixedNowUtc));

        var state = await manager.ResolveAsync(CreateIdentity(), CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.ActivationRequired, state.Status);
        Assert.False(state.Active);
        Assert.False(File.Exists(Path.Combine(_dataDirectory, CommercialLicenseConstants.FileName)));
    }

    [Fact]
    public async Task ResolveAsync_WhenTokenExistsButPublicKeyIsMissing_ReturnsLicenseKeyNotConfigured()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var token = CreateToken(keyPair, identity);
        var store = new CommercialLicenseStore(new CommercialLicenseStoreOptions(_dataDirectory));
        await store.WriteAsync(token, CancellationToken.None);
        var manager = CreateManager(_dataDirectory, new NotConfiguredPublicKeyProvider(), new MutableClock(FixedNowUtc));

        var state = await manager.ResolveAsync(identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.LicenseKeyNotConfigured, state.Status);
        Assert.False(state.Active);
    }

    [Fact]
    public async Task ActivateAsync_WhenLicenseIsValid_PersistsTokenAndNewManagerLoadsActiveState()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var clock = new MutableClock(FixedNowUtc);
        var token = CreateToken(keyPair, identity);
        var manager = CreateManager(_dataDirectory, keyPair, clock);

        var activation = await manager.ActivateAsync(token, identity, CancellationToken.None);

        Assert.True(activation.Activated);
        var filePath = Path.Combine(_dataDirectory, CommercialLicenseConstants.FileName);
        Assert.True(File.Exists(filePath));
        Assert.Equal(token, (await File.ReadAllTextAsync(filePath)).Trim());

        var secondManager = CreateManager(_dataDirectory, keyPair, clock);
        var state = await secondManager.ResolveAsync(identity, CancellationToken.None);

        Assert.Equal(CommercialLicenseStatus.Active, state.Status);
        Assert.Equal("license-1", state.LicenseId);
    }

    [Fact]
    public async Task ActivateAsync_WhenCandidateIsInvalid_DoesNotReplaceCurrentValidLicense()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var manager = CreateManager(_dataDirectory, keyPair, new MutableClock(FixedNowUtc));
        var validToken = CreateToken(
            keyPair,
            identity,
            mutate: payload => payload[JwtRegisteredClaimNames.Jti] = "license-a");
        var invalidToken = CreateToken(
            keyPair,
            identity,
            mutate: payload =>
            {
                payload[JwtRegisteredClaimNames.Jti] = "license-b";
                payload["product"] = "GALTEK_ONE";
            });

        var firstActivation = await manager.ActivateAsync(validToken, identity, CancellationToken.None);
        var secondActivation = await manager.ActivateAsync(invalidToken, identity, CancellationToken.None);

        Assert.True(firstActivation.Activated);
        Assert.False(secondActivation.Activated);
        Assert.Equal(CommercialLicenseStatus.ProductMismatch, secondActivation.CandidateState.Status);
        Assert.Equal("license-a", manager.CurrentState.LicenseId);
        Assert.Equal(CommercialLicenseStatus.Active, manager.CurrentState.Status);
        Assert.Equal(validToken, (await File.ReadAllTextAsync(Path.Combine(_dataDirectory, CommercialLicenseConstants.FileName))).Trim());
    }

    [Fact]
    public async Task ActivateAsync_WhenRenewalHasSameInstallationAndLaterExpiration_ReplacesCurrentLicense()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var manager = CreateManager(_dataDirectory, keyPair, new MutableClock(FixedNowUtc));
        var firstExpiration = FixedNowUtc.AddDays(30);
        var renewedExpiration = FixedNowUtc.AddDays(365);
        var firstToken = CreateToken(
            keyPair,
            identity,
            mutate: payload =>
            {
                payload[JwtRegisteredClaimNames.Jti] = "license-2027";
                payload[JwtRegisteredClaimNames.Exp] = firstExpiration.ToUnixTimeSeconds();
            });
        var renewalToken = CreateToken(
            keyPair,
            identity,
            mutate: payload =>
            {
                payload[JwtRegisteredClaimNames.Jti] = "license-2028";
                payload[JwtRegisteredClaimNames.Exp] = renewedExpiration.ToUnixTimeSeconds();
            });

        await manager.ActivateAsync(firstToken, identity, CancellationToken.None);
        var renewal = await manager.ActivateAsync(renewalToken, identity, CancellationToken.None);

        Assert.True(renewal.Activated);
        Assert.Equal("license-2028", manager.CurrentState.LicenseId);
        Assert.Equal(renewedExpiration, manager.CurrentState.ExpiresAtUtc);
        Assert.Equal(renewalToken, (await File.ReadAllTextAsync(Path.Combine(_dataDirectory, CommercialLicenseConstants.FileName))).Trim());
    }

    [Fact]
    public async Task RefreshExpirationOnly_WhenActiveLicenseReachesExpiration_ChangesStateToExpired()
    {
        using var keyPair = new TestLicenseKeyPair();
        var identity = CreateIdentity();
        var clock = new MutableClock(FixedNowUtc);
        var manager = CreateManager(_dataDirectory, keyPair, clock);
        var expiration = FixedNowUtc.AddMinutes(5);
        var token = CreateToken(
            keyPair,
            identity,
            mutate: payload => payload[JwtRegisteredClaimNames.Exp] = expiration.ToUnixTimeSeconds());

        await manager.ActivateAsync(token, identity, CancellationToken.None);
        clock.UtcNow = expiration;

        var state = manager.RefreshExpirationOnly();

        Assert.Equal(CommercialLicenseStatus.LicenseExpired, state.Status);
        Assert.False(state.Active);
        Assert.Equal("license-1", state.LicenseId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private static CommercialLicenseValidator CreateValidator(
        TestLicenseKeyPair keyPair,
        ISystemClock clock)
    {
        var identity = CreateIdentity();

        return new CommercialLicenseValidator(
            keyPair.CreatePublicKeyProvider(),
            clock,
            new StaticHardwareFingerprintProvider(ToFingerprint(identity)));
    }

    private static CommercialLicenseManager CreateManager(
        string dataDirectory,
        TestLicenseKeyPair keyPair,
        ISystemClock clock)
    {
        return CreateManager(dataDirectory, keyPair.CreatePublicKeyProvider(), clock);
    }

    private static CommercialLicenseManager CreateManager(
        string dataDirectory,
        ILicensePublicKeyProvider publicKeyProvider,
        ISystemClock clock)
    {
        var identity = CreateIdentity();
        var store = new CommercialLicenseStore(new CommercialLicenseStoreOptions(dataDirectory));
        var validator = new CommercialLicenseValidator(
            publicKeyProvider,
            clock,
            new StaticHardwareFingerprintProvider(ToFingerprint(identity)));

        return new CommercialLicenseManager(store, validator, clock);
    }

    private static InstallationIdentity CreateIdentity(string suffix = "1")
    {
        return InstallationIdentity.Create(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            HardwareFingerprintFactory.FromRawValues(
                [$"CPU SERIAL {suffix}"],
                [$"MOTHERBOARD SERIAL {suffix}"],
                [$"AA11BB22CC3{suffix}"],
                [$"DISK SERIAL {suffix}"]),
            FixedNowUtc.AddDays(-1));
    }

    private static string CreateToken(
        TestLicenseKeyPair keyPair,
        InstallationIdentity identity,
        Action<JwtPayload>? mutate = null,
        SigningCredentials? signingCredentials = null)
    {
        var issuedAtUtc = FixedNowUtc.AddMinutes(-5);
        var expiresAtUtc = FixedNowUtc.AddDays(365);
        var payload = new JwtPayload
        {
            { JwtRegisteredClaimNames.Iss, CommercialLicenseConstants.Issuer },
            { JwtRegisteredClaimNames.Aud, CommercialLicenseConstants.Audience },
            { JwtRegisteredClaimNames.Sub, identity.InstallationId.ToString("D") },
            { JwtRegisteredClaimNames.Jti, "license-1" },
            { "product", ProductInfo.ProductCode },
            { "schemaVersion", CommercialLicenseConstants.SchemaVersion },
            { "organizationId", "ESC-00042" },
            { "cpuHash", identity.CpuHash },
            { "motherboardHash", identity.MotherboardHash },
            { "macHash", identity.MacHash },
            { "diskHash", identity.DiskHash },
            { "roles", new[] { CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole } },
            {
                "features",
                new Dictionary<string, object?>
                {
                    ["screenMonitoring"] = true,
                    ["screenProjection"] = true,
                    ["inputLock"] = true,
                    ["remoteAppLaunch"] = true,
                    ["remoteShutdown"] = true,
                    ["multiMaster"] = false,
                    ["maxManagedClients"] = 30
                }
            },
            { JwtRegisteredClaimNames.Iat, issuedAtUtc.ToUnixTimeSeconds() },
            { JwtRegisteredClaimNames.Exp, expiresAtUtc.ToUnixTimeSeconds() }
        };

        mutate?.Invoke(payload);

        var token = new JwtSecurityToken(
            new JwtHeader(signingCredentials ?? keyPair.CreateSigningCredentials()),
            payload);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string DifferentHash(string value)
    {
        return Sha256Hasher.Hash($"DIFFERENT-{value}");
    }

    private static HardwareFingerprint ToFingerprint(InstallationIdentity identity)
    {
        return new HardwareFingerprint(
            identity.CpuHash,
            identity.MotherboardHash,
            identity.MacHash,
            identity.DiskHash);
    }

    private sealed class TestLicenseKeyPair : IDisposable
    {
        private readonly RSA _rsa = RSA.Create(2048);

        public SigningCredentials CreateSigningCredentials()
        {
            return new SigningCredentials(
                new RsaSecurityKey(_rsa),
                SecurityAlgorithms.RsaSha256);
        }

        public ILicensePublicKeyProvider CreatePublicKeyProvider()
        {
            var parameters = _rsa.ExportParameters(includePrivateParameters: false);
            var publicRsa = RSA.Create();
            publicRsa.ImportParameters(parameters);

            return new StaticPublicKeyProvider(new RsaSecurityKey(publicRsa));
        }

        public void Dispose()
        {
            _rsa.Dispose();
        }
    }

    private sealed class StaticPublicKeyProvider : ILicensePublicKeyProvider
    {
        private readonly SecurityKey _securityKey;

        public StaticPublicKeyProvider(SecurityKey securityKey)
        {
            _securityKey = securityKey;
        }

        public Task<LicensePublicKeyResult> GetPublicKeyAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(LicensePublicKeyResult.Configured(_securityKey));
        }
    }

    private sealed class NotConfiguredPublicKeyProvider : ILicensePublicKeyProvider
    {
        public Task<LicensePublicKeyResult> GetPublicKeyAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(LicensePublicKeyResult.NotConfigured("No test key configured."));
        }
    }

    private sealed class StaticHardwareFingerprintProvider : IHardwareFingerprintProvider
    {
        private readonly HardwareFingerprint _fingerprint;

        public StaticHardwareFingerprintProvider(HardwareFingerprint fingerprint)
        {
            _fingerprint = fingerprint;
        }

        public Task<HardwareFingerprint> GetCurrentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_fingerprint);
        }
    }

    private sealed class MutableClock : ISystemClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }
}
