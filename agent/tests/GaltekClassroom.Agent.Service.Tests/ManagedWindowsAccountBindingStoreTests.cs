using System.Text.Json;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class ManagedWindowsAccountBindingStoreTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherInstallationId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private const string PrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-1004";
    private const string SecondarySid = "S-1-5-21-1000000000-1000000000-1000000000-1005";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.ManagedWindowsAccounts.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_WhenFileIsMissing_ReturnsEmptyCatalog()
    {
        var result = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.True(result.Loaded);
        Assert.Empty(result.Bindings);
        Assert.False(File.Exists(BindingFilePath()));
    }

    [Fact]
    public async Task AddAsync_WhenFirstBindingIsValid_CreatesCatalog()
    {
        var write = await CreateStore().AddAsync(
            InstallationId,
            PrimaryBinding(),
            CancellationToken.None);
        var reopened = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.True(write.Succeeded);
        Assert.True(File.Exists(BindingFilePath()));
        Assert.Single(reopened.Bindings);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Primary, reopened.Bindings[0].AccountId);
        Assert.Equal(PrimarySid, reopened.Bindings[0].WindowsSid);
    }

    [Fact]
    public async Task LoadAsync_AfterWrite_PreservesBindingAndRemovesTemporaryFiles()
    {
        await CreateStore().AddAsync(InstallationId, PrimaryBinding(), CancellationToken.None);

        var reopened = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.True(reopened.Loaded);
        Assert.Equal("PC23\\Primaria", reopened.Bindings.Single().AccountReference);
        Assert.Empty(Directory.EnumerateFiles(_dataDirectory, "*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_WhenSchemaIsUnknown_ReturnsInvalid()
    {
        await WriteRawDocumentAsync(new
        {
            schemaVersion = 99,
            installationId = InstallationId,
            bindings = Array.Empty<object>()
        });

        var result = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreReadStatus.Invalid, result.Status);
        Assert.Equal(ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingsInvalid, result.ErrorCode);
        Assert.Contains("unsupported schemaVersion", result.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenJsonIsCorrupt_ReturnsInvalidAndPreservesFile()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(BindingFilePath(), "{ not-json");

        var result = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreReadStatus.Invalid, result.Status);
        Assert.Equal("{ not-json", await File.ReadAllTextAsync(BindingFilePath()));
    }

    [Fact]
    public async Task LoadAsync_WhenInstallationIdMismatches_ReturnsInvalid()
    {
        await WriteDocumentAsync(OtherInstallationId, [PrimaryBinding()]);

        var result = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("another installationId", result.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenPrimaryIsDuplicated_ReturnsInvalid()
    {
        await WriteDocumentAsync(InstallationId, [PrimaryBinding(), PrimaryBinding("PC23\\Primaria2")]);

        var result = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("duplicate PRIMARY", result.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenSecondaryIsDuplicated_ReturnsInvalid()
    {
        await WriteDocumentAsync(InstallationId, [SecondaryBinding(), SecondaryBinding("PC23\\Secundaria2")]);

        var result = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("duplicate SECONDARY", result.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenSameSidIsUsedByBothSlots_ReturnsInvalid()
    {
        await WriteDocumentAsync(InstallationId, [PrimaryBinding(), SecondaryBinding("PC23\\Secundaria", PrimarySid)]);

        var result = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("same SID", result.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenSidIsInvalid_ReturnsInvalid()
    {
        await WriteDocumentAsync(InstallationId, [PrimaryBinding(windowsSid: "not-a-sid")]);

        var result = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("windowsSid is invalid", result.ErrorMessage);
    }

    [Fact]
    public async Task AddAsync_WhenCatalogIsInvalid_DoesNotRegenerateFile()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(BindingFilePath(), "{ corrupt");

        var result = await CreateStore().AddAsync(InstallationId, PrimaryBinding(), CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreWriteStatus.StoreInvalid, result.Status);
        Assert.Equal("{ corrupt", await File.ReadAllTextAsync(BindingFilePath()));
    }

    [Fact]
    public async Task LoadAsync_WhenForbiddenCredentialFieldExists_ReturnsInvalidAndPreservesFile()
    {
        var forbidden = JsonSerializer.Serialize(new
        {
            schemaVersion = ManagedWindowsAccountBindingConstants.SchemaVersion,
            installationId = InstallationId,
            bindings = new object[]
            {
                new
                {
                    accountId = ClassroomManagedWindowsAccountTypes.Primary,
                    windowsSid = PrimarySid,
                    accountReference = "PC23\\Primaria",
                    credentialId = "forbidden",
                    createdAtUtc = FixedNowUtc,
                    updatedAtUtc = FixedNowUtc
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(BindingFilePath(), forbidden);

        var result = await CreateStore().LoadAsync(InstallationId, CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreReadStatus.Invalid, result.Status);
        Assert.Equal(forbidden, await File.ReadAllTextAsync(BindingFilePath()));
    }

    [Fact]
    public async Task AddAsync_WhenDuplicateWithoutReplace_ReturnsAlreadyExists()
    {
        var store = CreateStore();
        await store.AddAsync(InstallationId, PrimaryBinding(), CancellationToken.None);

        var duplicate = await store.AddAsync(
            InstallationId,
            PrimaryBinding("PC23\\Primaria2026", SecondarySid),
            CancellationToken.None);
        var reopened = await store.LoadAsync(InstallationId, CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreWriteStatus.AlreadyExists, duplicate.Status);
        Assert.Equal(PrimarySid, reopened.Bindings.Single().WindowsSid);
    }

    [Fact]
    public async Task ReplaceAsync_WhenSlotExists_ChangesSidExplicitly()
    {
        var store = CreateStore();
        await store.AddAsync(InstallationId, PrimaryBinding(), CancellationToken.None);

        var replacement = await store.ReplaceAsync(
            InstallationId,
            PrimaryBinding("PC23\\PrimariaNueva", SecondarySid),
            CancellationToken.None);
        var reopened = await store.LoadAsync(InstallationId, CancellationToken.None);

        Assert.True(replacement.Succeeded);
        Assert.Equal(SecondarySid, reopened.Bindings.Single().WindowsSid);
    }

    [Fact]
    public async Task AddAsync_WhenOtherSlotUsesSameSid_ReturnsConflict()
    {
        var store = CreateStore();
        await store.AddAsync(InstallationId, PrimaryBinding(), CancellationToken.None);

        var result = await store.AddAsync(
            InstallationId,
            SecondaryBinding("PC23\\Secundaria", PrimarySid),
            CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreWriteStatus.Conflict, result.Status);
        Assert.Equal(ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingConflict, result.ErrorCode);
    }

    [Fact]
    public async Task RemoveAsync_WhenPrimaryExists_RemovesOnlyPrimary()
    {
        var store = CreateStore();
        await store.AddAsync(InstallationId, PrimaryBinding(), CancellationToken.None);
        await store.AddAsync(InstallationId, SecondaryBinding(), CancellationToken.None);

        var result = await store.RemoveAsync(
            InstallationId,
            ClassroomManagedWindowsAccountTypes.Primary,
            CancellationToken.None);
        var reopened = await store.LoadAsync(InstallationId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(reopened.Bindings);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Secondary, reopened.Bindings[0].AccountId);
    }

    [Fact]
    public async Task RemoveAsync_WhenSecondaryExists_RemovesOnlySecondary()
    {
        var store = CreateStore();
        await store.AddAsync(InstallationId, PrimaryBinding(), CancellationToken.None);
        await store.AddAsync(InstallationId, SecondaryBinding(), CancellationToken.None);

        var result = await store.RemoveAsync(
            InstallationId,
            ClassroomManagedWindowsAccountTypes.Secondary,
            CancellationToken.None);
        var reopened = await store.LoadAsync(InstallationId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(reopened.Bindings);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Primary, reopened.Bindings[0].AccountId);
    }

    [Fact]
    public async Task RemoveAsync_WhenBindingDoesNotExist_ReturnsNotFound()
    {
        var result = await CreateStore().RemoveAsync(
            InstallationId,
            ClassroomManagedWindowsAccountTypes.Primary,
            CancellationToken.None);

        Assert.Equal(ManagedWindowsAccountBindingStoreWriteStatus.NotFound, result.Status);
        Assert.False(File.Exists(BindingFilePath()));
    }

    [Fact]
    public async Task AddAsync_AppliesFileSecurityToTempAndTarget()
    {
        var fileSecurity = new RecordingManagedWindowsAccountBindingFileSecurity();

        await CreateStore(fileSecurity).AddAsync(InstallationId, PrimaryBinding(), CancellationToken.None);

        Assert.Equal(2, fileSecurity.Paths.Count);
        Assert.Contains(fileSecurity.Paths, path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fileSecurity.Paths, path => path.EndsWith(ManagedWindowsAccountBindingConstants.FileName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PersistedJson_DoesNotContainPasswordsCredentialsOrTokens()
    {
        await CreateStore().AddAsync(InstallationId, PrimaryBinding(), CancellationToken.None);

        var json = await File.ReadAllTextAsync(BindingFilePath());

        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentialId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private ManagedWindowsAccountBindingStore CreateStore(
        IManagedWindowsAccountBindingFileSecurity? fileSecurity = null)
    {
        return new ManagedWindowsAccountBindingStore(
            new ManagedWindowsAccountBindingStoreOptions(_dataDirectory),
            fileSecurity ?? new NoOpManagedWindowsAccountBindingFileSecurity());
    }

    private string BindingFilePath()
    {
        return Path.Combine(_dataDirectory, ManagedWindowsAccountBindingConstants.FileName);
    }

    private static ManagedWindowsAccountBinding PrimaryBinding(
        string accountReference = "PC23\\Primaria",
        string windowsSid = PrimarySid)
    {
        return ManagedWindowsAccountBinding.Create(
            ClassroomManagedWindowsAccountTypes.Primary,
            windowsSid,
            accountReference,
            FixedNowUtc);
    }

    private static ManagedWindowsAccountBinding SecondaryBinding(
        string accountReference = "PC23\\Secundaria",
        string windowsSid = SecondarySid)
    {
        return ManagedWindowsAccountBinding.Create(
            ClassroomManagedWindowsAccountTypes.Secondary,
            windowsSid,
            accountReference,
            FixedNowUtc);
    }

    private async Task WriteDocumentAsync(
        Guid installationId,
        IReadOnlyList<ManagedWindowsAccountBinding> bindings)
    {
        await WriteRawDocumentAsync(new
        {
            schemaVersion = ManagedWindowsAccountBindingConstants.SchemaVersion,
            installationId,
            bindings
        });
    }

    private async Task WriteRawDocumentAsync(object document)
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            BindingFilePath(),
            JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private sealed class RecordingManagedWindowsAccountBindingFileSecurity : IManagedWindowsAccountBindingFileSecurity
    {
        public List<string> Paths { get; } = [];

        public void Apply(string filePath)
        {
            Paths.Add(filePath);
        }
    }
}
