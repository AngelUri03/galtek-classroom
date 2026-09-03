using System.Text.Json;
using GaltekClassroom.Agent.Session.Commands;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class SessionApplicationResolverTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.SessionApplicationResolver.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ResolveAsync_WhenCatalogIsMissing_ReturnsNotFound()
    {
        var registry = new RecordingAppPathsRegistry();

        var result = await CreateResolver(registry).ResolveAsync("word", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApplicationBindingErrorCodes.ApplicationBindingNotFound, result.ErrorCode);
        Assert.Empty(registry.Calls);
    }

    [Fact]
    public async Task ResolveAsync_WhenJsonIsCorrupt_ReturnsBindingsInvalid()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(CatalogPath(), "{ not-json");

        var result = await CreateResolver().ResolveAsync("word", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApplicationBindingErrorCodes.ApplicationBindingsInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task ResolveAsync_WhenSchemaIsUnknown_ReturnsBindingsInvalid()
    {
        await WriteCatalogAsync(new { schemaVersion = 99, bindings = Array.Empty<object>() });

        var result = await CreateResolver().ResolveAsync("word", CancellationToken.None);

        Assert.Equal(ApplicationBindingErrorCodes.ApplicationBindingsInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task ResolveAsync_WhenApplicationIdDoesNotExist_ReturnsNotFound()
    {
        await WriteCatalogAsync(Catalog(AppPath("scratch", "scratch.exe")));

        var result = await CreateResolver().ResolveAsync("word", CancellationToken.None);

        Assert.Equal(ApplicationBindingErrorCodes.ApplicationBindingNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task ResolveAsync_WhenBindingIsDisabled_DoesNotResolveTarget()
    {
        var registry = new RecordingAppPathsRegistry();
        await WriteCatalogAsync(Catalog(AppPath("word", "WINWORD.EXE", enabled: false)));

        var result = await CreateResolver(registry).ResolveAsync("word", CancellationToken.None);

        Assert.Equal(ApplicationBindingErrorCodes.ApplicationDisabled, result.ErrorCode);
        Assert.Empty(registry.Calls);
    }

    [Fact]
    public async Task ResolveAsync_WhenAbsoluteExeExists_ReturnsPath()
    {
        var executablePath = CreateDummyExe("Conejito.exe");
        await WriteCatalogAsync(Catalog(AbsoluteExe("conejito-lector", executablePath)));

        var result = await CreateResolver().ResolveAsync("conejito-lector", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFullPath(executablePath), result.ExecutablePath);
    }

    [Fact]
    public async Task ResolveAsync_WhenAbsoluteExeDisappeared_ReturnsExecutableNotFound()
    {
        var executablePath = Path.Combine(_dataDirectory, "Missing.exe");
        await WriteCatalogAsync(Catalog(AbsoluteExe("missing-app", executablePath)));

        var result = await CreateResolver().ResolveAsync("missing-app", CancellationToken.None);

        Assert.Equal(ApplicationBindingErrorCodes.ApplicationExecutableNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task ResolveAsync_WhenCatalogTargetIsInvalid_ReturnsBindingsInvalid()
    {
        await WriteCatalogAsync(Catalog(AbsoluteExe("bad-app", @"C:\Tools\App.bat")));

        var result = await CreateResolver().ResolveAsync("bad-app", CancellationToken.None);

        Assert.Equal(ApplicationBindingErrorCodes.ApplicationBindingsInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task ResolveAsync_WhenRegistry64HasValidAppPath_ReturnsResolvedPath()
    {
        var executablePath = CreateDummyExe("Word.exe");
        var registry = new RecordingAppPathsRegistry();
        registry.Values[(WindowsAppPathsRegistryView.Registry64, "WINWORD.EXE")] = executablePath;
        await WriteCatalogAsync(Catalog(AppPath("word", "WINWORD.EXE")));

        var result = await CreateResolver(registry).ResolveAsync("word", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFullPath(executablePath), result.ExecutablePath);
        if (Environment.Is64BitOperatingSystem)
        {
            Assert.Contains(registry.Calls, call => call.View == WindowsAppPathsRegistryView.Registry64);
        }
    }

    [Fact]
    public async Task ResolveAsync_WhenRegistry32HasValidAppPath_ReturnsResolvedPath()
    {
        var executablePath = CreateDummyExe("Word32.exe");
        var registry = new RecordingAppPathsRegistry();
        registry.Values[(WindowsAppPathsRegistryView.Registry32, "WINWORD.EXE")] = executablePath;
        await WriteCatalogAsync(Catalog(AppPath("word", "WINWORD.EXE")));

        var result = await CreateResolver(registry).ResolveAsync("word", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFullPath(executablePath), result.ExecutablePath);
        Assert.Contains(registry.Calls, call => call.View == WindowsAppPathsRegistryView.Registry32);
    }

    [Fact]
    public async Task ResolveAsync_WhenBothRegistryViewsPointToDifferentPaths_FailsClosed()
    {
        var executablePath64 = CreateDummyExe("Word64.exe");
        var executablePath32 = CreateDummyExe("Word32.exe");
        var registry = new RecordingAppPathsRegistry();
        registry.Values[(WindowsAppPathsRegistryView.Registry64, "WINWORD.EXE")] = executablePath64;
        registry.Values[(WindowsAppPathsRegistryView.Registry32, "WINWORD.EXE")] = executablePath32;
        await WriteCatalogAsync(Catalog(AppPath("word", "WINWORD.EXE")));

        var result = await CreateResolver(registry).ResolveAsync("word", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApplicationBindingErrorCodes.ApplicationBindingInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task ResolveAsync_WhenRegistryTargetDoesNotExist_ReturnsExecutableNotFound()
    {
        var registry = new RecordingAppPathsRegistry();
        registry.Values[(WindowsAppPathsRegistryView.Registry64, "WINWORD.EXE")] = Path.Combine(_dataDirectory, "missing.exe");
        await WriteCatalogAsync(Catalog(AppPath("word", "WINWORD.EXE")));

        var result = await CreateResolver(registry).ResolveAsync("word", CancellationToken.None);

        Assert.Equal(ApplicationBindingErrorCodes.ApplicationExecutableNotFound, result.ErrorCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private SessionApplicationResolver CreateResolver(IWindowsAppPathsRegistry? registry = null)
    {
        return new SessionApplicationResolver(
            new SessionApplicationResolverOptions(_dataDirectory),
            registry ?? new RecordingAppPathsRegistry());
    }

    private string CreateDummyExe(string fileName)
    {
        Directory.CreateDirectory(_dataDirectory);
        var path = Path.Combine(_dataDirectory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static object Catalog(params object[] bindings)
    {
        return new
        {
            schemaVersion = ApplicationBindingConstants.SchemaVersion,
            bindings
        };
    }

    private static object AppPath(string applicationId, string executableName, bool enabled = true)
    {
        return new
        {
            applicationId,
            launchType = "APP_PATHS",
            appPathExecutableName = executableName,
            executablePath = (string?)null,
            enabled,
            createdAtUtc = FixedNowUtc,
            updatedAtUtc = FixedNowUtc
        };
    }

    private static object AbsoluteExe(string applicationId, string executablePath)
    {
        return new
        {
            applicationId,
            launchType = "ABSOLUTE_EXE",
            appPathExecutableName = (string?)null,
            executablePath,
            enabled = true,
            createdAtUtc = FixedNowUtc,
            updatedAtUtc = FixedNowUtc
        };
    }

    private async Task WriteCatalogAsync(object catalog)
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            CatalogPath(),
            JsonSerializer.Serialize(catalog, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private string CatalogPath()
    {
        return Path.Combine(_dataDirectory, ApplicationBindingConstants.FileName);
    }

    private sealed class RecordingAppPathsRegistry : IWindowsAppPathsRegistry
    {
        public Dictionary<(WindowsAppPathsRegistryView View, string Name), string> Values { get; } = [];
        public List<(WindowsAppPathsRegistryView View, string Name)> Calls { get; } = [];

        public string? GetHklmDefaultExecutablePath(string executableName, WindowsAppPathsRegistryView view)
        {
            Calls.Add((view, executableName));
            return Values.TryGetValue((view, executableName), out var value)
                ? value
                : null;
        }
    }
}
