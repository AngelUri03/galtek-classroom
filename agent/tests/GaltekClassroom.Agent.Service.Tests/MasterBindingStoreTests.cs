using System.Text.Json;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class MasterBindingStoreTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
    private const string ValidSid = "S-1-5-21-1000000000-1000000000-1000000000-1001";
    private const string SecondValidSid = "S-1-5-21-1000000000-1000000000-1000000000-1002";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.MasterBinding.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadAsync_WhenFileIsMissing_ReturnsMissing()
    {
        var store = CreateStore();

        var result = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(MasterBindingStoreReadStatus.Missing, result.Status);
        Assert.Null(result.Binding);
        Assert.EndsWith(MasterBindingConstants.FileName, result.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WhenBindingIsValid_PersistsAndReopensIt()
    {
        var store = CreateStore();
        var binding = CreateBinding(ValidSid, "AULA\\MaestraPrimaria");

        var write = await store.WriteAsync(binding, replaceExisting: false, CancellationToken.None);
        var reopened = await CreateStore().ReadAsync(CancellationToken.None);

        Assert.True(write.Configured);
        Assert.Equal(MasterBindingStoreReadStatus.Loaded, reopened.Status);
        Assert.Equal(InstallationId, reopened.Binding!.InstallationId);
        Assert.Equal(ValidSid, reopened.Binding.WindowsSid);
        Assert.Equal("AULA\\MaestraPrimaria", reopened.Binding.AccountDisplayName);
    }

    [Fact]
    public async Task ReadAsync_WhenFileIsCorrupt_ReturnsInvalidAndPreservesFile()
    {
        Directory.CreateDirectory(_dataDirectory);
        var filePath = Path.Combine(_dataDirectory, MasterBindingConstants.FileName);
        await File.WriteAllTextAsync(filePath, "{ not-json");
        var store = CreateStore();

        var result = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(MasterBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("corrupt or incomplete", result.ErrorMessage);
        Assert.Equal("{ not-json", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task ReadAsync_WhenSchemaIsUnknown_ReturnsInvalid()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, MasterBindingConstants.FileName),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 99,
                installationId = InstallationId,
                windowsSid = ValidSid,
                accountDisplayName = "AULA\\MaestraPrimaria",
                boundAtUtc = FixedNowUtc
            }));

        var result = await CreateStore().ReadAsync(CancellationToken.None);

        Assert.Equal(MasterBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("unsupported schemaVersion", result.ErrorMessage);
    }

    [Fact]
    public async Task ReadAsync_WhenSidIsInvalid_ReturnsInvalid()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, MasterBindingConstants.FileName),
            JsonSerializer.Serialize(new
            {
                schemaVersion = MasterBindingConstants.SchemaVersion,
                installationId = InstallationId,
                windowsSid = "not-a-sid",
                accountDisplayName = "AULA\\MaestraPrimaria",
                boundAtUtc = FixedNowUtc
            }));

        var result = await CreateStore().ReadAsync(CancellationToken.None);

        Assert.Equal(MasterBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("windowsSid is invalid", result.ErrorMessage);
    }

    [Fact]
    public async Task WriteAsync_WhenBindingExistsWithoutReplace_DoesNotOverwrite()
    {
        var store = CreateStore();
        await store.WriteAsync(
            CreateBinding(ValidSid, "AULA\\MaestraA"),
            replaceExisting: false,
            CancellationToken.None);

        var write = await store.WriteAsync(
            CreateBinding(SecondValidSid, "AULA\\MaestraB"),
            replaceExisting: false,
            CancellationToken.None);
        var reopened = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(MasterBindingStoreWriteStatus.AlreadyConfigured, write.Status);
        Assert.Equal("AULA\\MaestraA", reopened.Binding!.AccountDisplayName);
        Assert.Equal(ValidSid, reopened.Binding.WindowsSid);
    }

    [Fact]
    public async Task WriteAsync_WhenReplaceIsExplicit_ReplacesAfterVerification()
    {
        var store = CreateStore();
        await store.WriteAsync(
            CreateBinding(ValidSid, "AULA\\MaestraA"),
            replaceExisting: false,
            CancellationToken.None);

        var write = await store.WriteAsync(
            CreateBinding(SecondValidSid, "AULA\\MaestraB"),
            replaceExisting: true,
            CancellationToken.None);
        var reopened = await store.ReadAsync(CancellationToken.None);

        Assert.True(write.Configured);
        Assert.Equal("AULA\\MaestraB", reopened.Binding!.AccountDisplayName);
        Assert.Equal(SecondValidSid, reopened.Binding.WindowsSid);
    }

    [Fact]
    public async Task WriteAsync_WhenCandidateIsInvalid_DoesNotDestroyPreviousBinding()
    {
        var store = CreateStore();
        await store.WriteAsync(
            CreateBinding(ValidSid, "AULA\\MaestraA"),
            replaceExisting: false,
            CancellationToken.None);

        var invalidCandidate = CreateBinding("not-a-sid", "AULA\\MaestraB");

        var write = await store.WriteAsync(invalidCandidate, replaceExisting: true, CancellationToken.None);
        var reopened = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(MasterBindingStoreWriteStatus.InvalidCandidate, write.Status);
        Assert.Equal("AULA\\MaestraA", reopened.Binding!.AccountDisplayName);
        Assert.Equal(ValidSid, reopened.Binding.WindowsSid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private MasterBindingStore CreateStore()
    {
        return new MasterBindingStore(
            new MasterBindingStoreOptions(_dataDirectory),
            new NoOpMasterBindingFileSecurity());
    }

    private static MasterWindowsBinding CreateBinding(string sid, string accountDisplayName)
    {
        return MasterWindowsBinding.Create(
            InstallationId,
            sid,
            accountDisplayName,
            FixedNowUtc);
    }
}
