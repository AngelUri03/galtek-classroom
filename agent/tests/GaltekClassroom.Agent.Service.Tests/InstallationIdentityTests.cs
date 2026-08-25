using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class InstallationIdentityTests : IDisposable
{
    private static readonly DateTimeOffset FixedCreatedAtUtc = new(2026, 8, 25, 12, 30, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ResolveAsync_WhenIdentityDoesNotExist_CreatesAndPersistsIdentity()
    {
        var resolver = CreateResolver(_dataDirectory);

        var result = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(InstallationIdentityResolutionStatus.Ready, result.Status);
        Assert.True(result.Created);
        Assert.NotNull(result.Identity);
        Assert.NotEqual(Guid.Empty, result.Identity.InstallationId);
        AssertValidFingerprintHashes(result.Identity);
        Assert.True(File.Exists(Path.Combine(_dataDirectory, InstallationIdentityConstants.FileName)));
    }

    [Fact]
    public async Task ResolveAsync_WhenIdentityExists_PreservesInstallationIdAndCreatedAt()
    {
        var firstProvider = new FakeHardwareFingerprintProvider(CreateFakeFingerprint());
        var firstResolver = CreateResolver(_dataDirectory, firstProvider);

        var firstResult = await firstResolver.ResolveAsync(CancellationToken.None);
        var firstIdentity = firstResult.Identity!;

        var secondProvider = new FakeHardwareFingerprintProvider(CreateFakeFingerprint("changed"));
        var secondResolver = CreateResolver(_dataDirectory, secondProvider);

        var secondResult = await secondResolver.ResolveAsync(CancellationToken.None);
        var secondIdentity = secondResult.Identity!;

        Assert.False(secondResult.Created);
        Assert.Equal(firstIdentity.InstallationId, secondIdentity.InstallationId);
        Assert.Equal(firstIdentity.CreatedAtUtc, secondIdentity.CreatedAtUtc);
        Assert.Equal(0, secondProvider.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_WhenIdentityFileIsCorrupt_ReturnsControlledErrorWithoutRegenerating()
    {
        Directory.CreateDirectory(_dataDirectory);
        var filePath = Path.Combine(_dataDirectory, InstallationIdentityConstants.FileName);
        await File.WriteAllTextAsync(filePath, "{ not-json");

        var provider = new FakeHardwareFingerprintProvider(CreateFakeFingerprint());
        var resolver = CreateResolver(_dataDirectory, provider);

        var result = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(InstallationIdentityResolutionStatus.Invalid, result.Status);
        Assert.Null(result.Identity);
        Assert.Contains("corrupt or incomplete", result.ErrorMessage);
        Assert.Equal(0, provider.CallCount);
        Assert.Equal("{ not-json", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public void Hash_WhenNormalizedInputIsEquivalent_ReturnsSameSha256()
    {
        var first = HardwareIdentifierNormalizer.NormalizeCollectionOrUnavailable([" cpu   serial  001 "]);
        var second = HardwareIdentifierNormalizer.NormalizeCollectionOrUnavailable(["CPU SERIAL 001"]);

        Assert.Equal(Sha256Hasher.Hash(first), Sha256Hasher.Hash(second));
    }

    [Fact]
    public void Normalization_TrimsCollapsesCaseFiltersPlaceholdersAndSorts()
    {
        Assert.Equal(
            HardwareIdentifierNormalizer.NormalizeSingle("  abc   def  "),
            HardwareIdentifierNormalizer.NormalizeSingle("ABC DEF"));

        var first = HardwareIdentifierNormalizer.NormalizeCollectionOrUnavailable(["  b 2", "a 1"]);
        var second = HardwareIdentifierNormalizer.NormalizeCollectionOrUnavailable(["A 1", "B   2"]);

        Assert.Equal("A 1|B 2", first);
        Assert.Equal(first, second);
        Assert.Null(HardwareIdentifierNormalizer.NormalizeSingle("To be filled by O.E.M."));
    }

    [Fact]
    public void MachineCode_WhenDecoded_ContainsExpectedPayloadWithoutSensitiveFields()
    {
        var identity = InstallationIdentity.Create(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            new HardwareFingerprint(
                Sha256Hasher.Hash("CPU-SERIAL-RAW"),
                Sha256Hasher.Hash("MOTHERBOARD-SERIAL-RAW"),
                Sha256Hasher.Hash("AA11BB22CC33"),
                Sha256Hasher.Hash("DISK-SERIAL-RAW")),
            FixedCreatedAtUtc);
        var generator = new MachineCodeGenerator(new FakeHostNameProvider("PC-AULA-07"));

        var machineCode = generator.Generate(identity);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(machineCode));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(ProductInfo.ProductCode, root.GetProperty("product").GetString());
        Assert.Equal(InstallationIdentityConstants.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(identity.InstallationId, root.GetProperty("installationId").GetGuid());
        Assert.Equal(identity.CpuHash, root.GetProperty("cpuHash").GetString());
        Assert.Equal(identity.MotherboardHash, root.GetProperty("motherboardHash").GetString());
        Assert.Equal(identity.MacHash, root.GetProperty("macHash").GetString());
        Assert.Equal(identity.DiskHash, root.GetProperty("diskHash").GetString());
        Assert.Equal("PC-AULA-07", root.GetProperty("hostname").GetString());
        Assert.DoesNotContain("CPU-SERIAL-RAW", json);
        Assert.DoesNotContain("MOTHERBOARD-SERIAL-RAW", json);
        Assert.DoesNotContain("DISK-SERIAL-RAW", json);
        Assert.DoesNotContain("licenseToken", json);
        Assert.DoesNotContain("192.168.1.20", json);
    }

    [Fact]
    public async Task ResolveAsync_WhenDataDirectoryIsConfigured_UsesConfiguredDirectoryOnly()
    {
        var resolvedDirectory = AgentDataDirectory.Resolve(name =>
            name == AgentDataDirectory.EnvironmentVariableName ? _dataDirectory : null);
        var resolver = CreateResolver(resolvedDirectory);

        var result = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(InstallationIdentityResolutionStatus.Ready, result.Status);
        Assert.True(File.Exists(Path.Combine(_dataDirectory, InstallationIdentityConstants.FileName)));
        Assert.False(resolvedDirectory.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private static InstallationIdentityResolver CreateResolver(
        string dataDirectory,
        FakeHardwareFingerprintProvider? provider = null)
    {
        var store = new InstallationIdentityStore(new InstallationIdentityStoreOptions(dataDirectory));

        return new InstallationIdentityResolver(
            store,
            provider ?? new FakeHardwareFingerprintProvider(CreateFakeFingerprint()),
            new FakeClock(FixedCreatedAtUtc));
    }

    private static HardwareFingerprint CreateFakeFingerprint(string suffix = "1")
    {
        return HardwareFingerprintFactory.FromRawValues(
            [$"CPU SERIAL {suffix}"],
            [$"MOTHERBOARD SERIAL {suffix}"],
            [$"AA11BB22CC3{suffix}"],
            [$"DISK SERIAL {suffix}"]);
    }

    private static void AssertValidFingerprintHashes(InstallationIdentity identity)
    {
        Assert.True(InstallationIdentityValidator.IsValidSha256Hex(identity.CpuHash));
        Assert.True(InstallationIdentityValidator.IsValidSha256Hex(identity.MotherboardHash));
        Assert.True(InstallationIdentityValidator.IsValidSha256Hex(identity.MacHash));
        Assert.True(InstallationIdentityValidator.IsValidSha256Hex(identity.DiskHash));
    }

    private sealed class FakeHardwareFingerprintProvider : IHardwareFingerprintProvider
    {
        private readonly HardwareFingerprint _fingerprint;

        public FakeHardwareFingerprintProvider(HardwareFingerprint fingerprint)
        {
            _fingerprint = fingerprint;
        }

        public int CallCount { get; private set; }

        public Task<HardwareFingerprint> GetCurrentAsync(CancellationToken cancellationToken)
        {
            CallCount++;

            return Task.FromResult(_fingerprint);
        }
    }

    private sealed class FakeClock : ISystemClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeHostNameProvider : IHostNameProvider
    {
        private readonly string _hostName;

        public FakeHostNameProvider(string hostName)
        {
            _hostName = hostName;
        }

        public string GetHostName()
        {
            return _hostName;
        }
    }
}
