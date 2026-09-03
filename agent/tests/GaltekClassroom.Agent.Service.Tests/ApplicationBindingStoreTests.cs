using System.Text.Json;
using GaltekClassroom.Agent.Service.Applications;
using GaltekClassroom.Agent.Service.Identity;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class ApplicationBindingStoreTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.ApplicationBindings.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_WhenFileIsMissing_ReturnsEmptyCatalog()
    {
        var result = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.True(result.Loaded);
        Assert.Empty(result.Bindings);
        Assert.False(File.Exists(Path.Combine(_dataDirectory, ApplicationBindingConstants.FileName)));
    }

    [Fact]
    public async Task AddAbsoluteExeAsync_WhenPathIsValid_PersistsBinding()
    {
        var executablePath = CreateDummyExe("Conejito.exe");

        var write = await CreateStore().AddAbsoluteExeAsync(
            "conejito-lector",
            executablePath,
            replaceExisting: false,
            CancellationToken.None);
        var reopened = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.True(write.Succeeded);
        Assert.Single(reopened.Bindings);
        Assert.Equal(ApplicationLaunchType.AbsoluteExe, reopened.Bindings[0].LaunchType);
        Assert.Equal(Path.GetFullPath(executablePath), reopened.Bindings[0].ExecutablePath);
        Assert.Null(reopened.Bindings[0].AppPathExecutableName);
        Assert.True(reopened.Bindings[0].Enabled);
    }

    [Fact]
    public async Task AddAppPathAsync_WhenExecutableNameIsValid_PersistsBinding()
    {
        var write = await CreateStore().AddAppPathAsync(
            "microsoft-word",
            "WINWORD.EXE",
            replaceExisting: false,
            CancellationToken.None);
        var reopened = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.True(write.Succeeded);
        Assert.Single(reopened.Bindings);
        Assert.Equal(ApplicationLaunchType.AppPaths, reopened.Bindings[0].LaunchType);
        Assert.Equal("WINWORD.EXE", reopened.Bindings[0].AppPathExecutableName);
        Assert.Null(reopened.Bindings[0].ExecutablePath);
    }

    [Fact]
    public async Task AddAppPathAsync_WhenDuplicateWithoutReplace_ReturnsConflict()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("scratch", "scratch.exe", replaceExisting: false, CancellationToken.None);

        var duplicate = await store.AddAppPathAsync("scratch", "scratch2.exe", replaceExisting: false, CancellationToken.None);
        var reopened = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationBindingStoreWriteStatus.AlreadyExists, duplicate.Status);
        Assert.Equal(ApplicationBindingErrorCodes.ApplicationBindingAlreadyExists, duplicate.ErrorCode);
        Assert.Equal("scratch.exe", reopened.Bindings[0].AppPathExecutableName);
    }

    [Fact]
    public async Task AddAppPathAsync_WhenReplaceIsExplicit_ReplacesBinding()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("scratch", "scratch.exe", replaceExisting: false, CancellationToken.None);

        var replacement = await store.AddAppPathAsync("scratch", "scratch3.exe", replaceExisting: true, CancellationToken.None);
        var reopened = await store.LoadAsync(CancellationToken.None);

        Assert.True(replacement.Succeeded);
        Assert.Equal("scratch3.exe", reopened.Bindings[0].AppPathExecutableName);
    }

    [Fact]
    public async Task SetEnabledAsync_WhenBindingExists_TogglesWithoutDeleting()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("robomind", "robomind.exe", replaceExisting: false, CancellationToken.None);

        await store.SetEnabledAsync("robomind", enabled: false, CancellationToken.None);
        var disabled = await store.LoadAsync(CancellationToken.None);
        await store.SetEnabledAsync("robomind", enabled: true, CancellationToken.None);
        var enabled = await store.LoadAsync(CancellationToken.None);

        Assert.False(disabled.Bindings[0].Enabled);
        Assert.True(enabled.Bindings[0].Enabled);
    }

    [Fact]
    public async Task ReloadAsync_AfterWrite_PreservesContentAndRemovesTemporaryFiles()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("excel", "excel.exe", replaceExisting: false, CancellationToken.None);

        var reopened = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.True(reopened.Loaded);
        Assert.Single(reopened.Bindings);
        Assert.Empty(Directory.EnumerateFiles(_dataDirectory, "*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_WhenJsonIsCorrupt_ReturnsInvalidAndPreservesFile()
    {
        Directory.CreateDirectory(_dataDirectory);
        var filePath = BindingFilePath();
        await File.WriteAllTextAsync(filePath, "{ not-json");

        var result = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationBindingStoreReadStatus.Invalid, result.Status);
        Assert.Equal(ApplicationBindingErrorCodes.ApplicationBindingsInvalid, result.ErrorCode);
        Assert.Equal("{ not-json", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task LoadAsync_WhenSchemaIsUnknown_ReturnsInvalid()
    {
        await WriteRawDocumentAsync(new
        {
            schemaVersion = 99,
            bindings = Array.Empty<object>()
        });

        var result = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("unsupported schemaVersion", result.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenDuplicateIdsExist_ReturnsInvalid()
    {
        await WriteRawDocumentAsync(new
        {
            schemaVersion = ApplicationBindingConstants.SchemaVersion,
            bindings = new object[]
            {
                new
                {
                    applicationId = "scratch",
                    launchType = "APP_PATHS",
                    appPathExecutableName = "scratch.exe",
                    executablePath = (string?)null,
                    enabled = true,
                    createdAtUtc = FixedNowUtc,
                    updatedAtUtc = FixedNowUtc
                },
                new
                {
                    applicationId = "SCRATCH",
                    launchType = "APP_PATHS",
                    appPathExecutableName = "scratch.exe",
                    executablePath = (string?)null,
                    enabled = true,
                    createdAtUtc = FixedNowUtc,
                    updatedAtUtc = FixedNowUtc
                }
            }
        });

        var result = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationBindingStoreReadStatus.Invalid, result.Status);
        Assert.Contains("duplicate", result.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenLaunchTypeIsUnknown_ReturnsInvalid()
    {
        await WriteRawDocumentAsync(new
        {
            schemaVersion = ApplicationBindingConstants.SchemaVersion,
            bindings = new object[]
            {
                new
                {
                    applicationId = "word",
                    launchType = "CUSTOM_COMMAND",
                    appPathExecutableName = "WINWORD.EXE",
                    executablePath = (string?)null,
                    enabled = true,
                    createdAtUtc = FixedNowUtc,
                    updatedAtUtc = FixedNowUtc
                }
            }
        });

        var result = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationBindingStoreReadStatus.Invalid, result.Status);
        Assert.Equal(ApplicationBindingErrorCodes.ApplicationBindingsInvalid, result.ErrorCode);
    }

    [Theory]
    [InlineData("WINWORD.EXE", true)]
    [InlineData(@"C:\foo.exe", false)]
    [InlineData("foo.exe /arg", false)]
    [InlineData(@"..\foo.exe", false)]
    [InlineData(@"\\server\app.exe", false)]
    public void ValidateAppPathExecutableName_EnforcesSafeFileNameOnly(
        string executableName,
        bool expectedValid)
    {
        var result = ApplicationBindingValidator.ValidateAppPathExecutableName(executableName);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void ValidateAbsoluteExePath_WhenLocalAbsoluteExeExists_IsValid()
    {
        var executablePath = CreateDummyExe("LegacyApp.exe");

        var result = ApplicationBindingValidator.ValidateAbsoluteExePath(executablePath, requireExists: true);

        Assert.True(result.IsValid);
        Assert.Equal(Path.GetFullPath(executablePath), result.NormalizedValue);
    }

    [Theory]
    [InlineData(@"Relative\App.exe")]
    [InlineData(@"\\server\share\App.exe")]
    [InlineData(@"C:\Tools\App.bat")]
    [InlineData(@"C:\Tools\App.cmd")]
    [InlineData(@"C:\Tools\App.ps1")]
    [InlineData(@"C:\Tools\App.msi")]
    [InlineData("C:\\Tools\\App\u0001.exe")]
    public void ValidateAbsoluteExePath_RejectsUnsafePaths(string executablePath)
    {
        var result = ApplicationBindingValidator.ValidateAbsoluteExePath(executablePath, requireExists: false);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task AddAbsoluteExeAsync_WhenFileDoesNotExist_ReturnsExecutableNotFound()
    {
        var store = CreateStore();
        var missingPath = Path.Combine(_dataDirectory, "missing.exe");

        var result = await store.AddAbsoluteExeAsync(
            "missing-app",
            missingPath,
            replaceExisting: false,
            CancellationToken.None);

        Assert.Equal(ApplicationBindingStoreWriteStatus.ExecutableNotFound, result.Status);
        Assert.Equal(ApplicationBindingErrorCodes.ApplicationExecutableNotFound, result.ErrorCode);
        Assert.False(File.Exists(BindingFilePath()));
    }

    [Fact]
    public async Task AddAppPathAsync_WhenCatalogIsCorrupt_DoesNotRegenerateFile()
    {
        Directory.CreateDirectory(_dataDirectory);
        var filePath = BindingFilePath();
        await File.WriteAllTextAsync(filePath, "{ corrupt");

        var result = await CreateStore().AddAppPathAsync(
            "word",
            "WINWORD.EXE",
            replaceExisting: false,
            CancellationToken.None);

        Assert.Equal(ApplicationBindingStoreWriteStatus.StoreInvalid, result.Status);
        Assert.Equal("{ corrupt", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task RemoveAsync_WhenBindingExists_RemovesIt()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);

        var result = await store.RemoveAsync("word", CancellationToken.None);
        var reopened = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationBindingStoreWriteStatus.Removed, result.Status);
        Assert.Empty(reopened.Bindings);
    }

    [Fact]
    public async Task AddAppPathAsync_AppliesFileSecurityToTempAndTarget()
    {
        var fileSecurity = new RecordingApplicationBindingFileSecurity();
        var store = CreateStore(fileSecurity);

        await store.AddAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);

        Assert.Equal(2, fileSecurity.Paths.Count);
        Assert.Contains(fileSecurity.Paths, path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fileSecurity.Paths, path => path.EndsWith(ApplicationBindingConstants.FileName, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private ApplicationBindingStore CreateStore(IApplicationBindingFileSecurity? fileSecurity = null)
    {
        return new ApplicationBindingStore(
            new ApplicationBindingStoreOptions(_dataDirectory),
            fileSecurity ?? new NoOpApplicationBindingFileSecurity(),
            new FakeClock(FixedNowUtc));
    }

    private string CreateDummyExe(string fileName)
    {
        Directory.CreateDirectory(_dataDirectory);
        var path = Path.Combine(_dataDirectory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private string BindingFilePath()
    {
        return Path.Combine(_dataDirectory, ApplicationBindingConstants.FileName);
    }

    private async Task WriteRawDocumentAsync(object document)
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            BindingFilePath(),
            JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private sealed class RecordingApplicationBindingFileSecurity : IApplicationBindingFileSecurity
    {
        public List<string> Paths { get; } = [];

        public void Apply(string filePath)
        {
            Paths.Add(filePath);
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
}
