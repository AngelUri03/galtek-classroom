using GaltekClassroom.Agent.Service.Applications;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Master;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class ApplicationBindingConfigurationServiceTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.ApplicationBindingConfig.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BindAppPathAsync_WhenNotElevated_ReturnsAdministratorRequiredAndDoesNotWrite()
    {
        var service = CreateService(isAdmin: false);

        var result = await service.BindAppPathAsync(
            "word",
            "WINWORD.EXE",
            replaceExisting: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApplicationBindingConfigurationStatus.AdministratorRequired, result.Status);
        Assert.False(File.Exists(BindingFilePath()));
    }

    [Fact]
    public async Task ListAsync_WhenNotElevated_ReturnsCatalogWithoutMutatingIt()
    {
        var elevated = CreateService(isAdmin: true);
        await elevated.BindAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);
        var original = await File.ReadAllTextAsync(BindingFilePath());

        var result = await CreateService(isAdmin: false).ListAsync(CancellationToken.None);
        var afterList = await File.ReadAllTextAsync(BindingFilePath());

        Assert.True(result.Succeeded);
        Assert.Single(result.Bindings);
        Assert.Equal(original, afterList);
    }

    [Fact]
    public async Task BindAppPathAsync_WhenDuplicateWithoutReplace_ReturnsAlreadyExists()
    {
        var service = CreateService(isAdmin: true);
        await service.BindAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);

        var result = await service.BindAppPathAsync(
            "word",
            "word2.exe",
            replaceExisting: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApplicationBindingConfigurationStatus.ApplicationBindingAlreadyExists, result.Status);
    }

    [Fact]
    public async Task BindAppPathAsync_WhenReplaceIsExplicit_UpdatesCatalog()
    {
        var service = CreateService(isAdmin: true);
        await service.BindAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);

        var result = await service.BindAppPathAsync(
            "word",
            "word2.exe",
            replaceExisting: true,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("word2.exe", result.Bindings.Single().AppPathExecutableName);
    }

    [Fact]
    public async Task SetEnabledAsync_WhenBindingDoesNotExist_ReturnsNotFound()
    {
        var result = await CreateService(isAdmin: true).SetEnabledAsync(
            "word",
            enabled: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApplicationBindingConfigurationStatus.ApplicationBindingNotFound, result.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private ApplicationBindingConfigurationService CreateService(bool isAdmin)
    {
        var store = new ApplicationBindingStore(
            new ApplicationBindingStoreOptions(_dataDirectory),
            new NoOpApplicationBindingFileSecurity(),
            new FakeClock(FixedNowUtc));

        return new ApplicationBindingConfigurationService(
            store,
            new FakeAdministratorPrivilegeChecker(isAdmin));
    }

    private string BindingFilePath()
    {
        return Path.Combine(_dataDirectory, ApplicationBindingConstants.FileName);
    }

    private sealed class FakeAdministratorPrivilegeChecker : IAdministratorPrivilegeChecker
    {
        private readonly bool _isAdmin;

        public FakeAdministratorPrivilegeChecker(bool isAdmin)
        {
            _isAdmin = isAdmin;
        }

        public bool IsElevatedAdministrator()
        {
            return _isAdmin;
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
