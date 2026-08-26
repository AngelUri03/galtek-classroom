using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class MasterBindingConfigurationServiceTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
    private const string FirstSid = "S-1-5-21-1000000000-1000000000-1000000000-1001";
    private const string SecondSid = "S-1-5-21-1000000000-1000000000-1000000000-1002";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.MasterBindingCli.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BindCurrentUserAsync_WhenNotElevated_ReturnsAdministratorRequired()
    {
        var service = CreateService(isAdmin: false);

        var result = await service.BindCurrentUserAsync(replaceExisting: false, CancellationToken.None);

        Assert.False(result.Configured);
        Assert.Equal(MasterBindingConfigurationStatus.AdministratorRequired, result.Status);
        Assert.False(File.Exists(Path.Combine(_dataDirectory, MasterBindingConstants.FileName)));
    }

    [Fact]
    public async Task BindCurrentUserAsync_WhenElevated_PersistsCurrentUser()
    {
        var service = CreateService(isAdmin: true);

        var result = await service.BindCurrentUserAsync(replaceExisting: false, CancellationToken.None);
        var binding = (await CreateStore().ReadAsync(CancellationToken.None)).Binding!;

        Assert.True(result.Configured);
        Assert.Equal("AULA\\MaestraPrimaria", result.AccountDisplayName);
        Assert.Equal(FirstSid, binding.WindowsSid);
        Assert.Equal("AULA\\MaestraPrimaria", binding.AccountDisplayName);
    }

    [Fact]
    public async Task BindAccountAsync_WhenAccountDoesNotExist_DoesNotModifyExistingBinding()
    {
        var store = CreateStore();
        await store.WriteAsync(
            MasterWindowsBinding.Create(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), FirstSid, "AULA\\MaestraA", FixedNowUtc),
            replaceExisting: false,
            CancellationToken.None);
        var resolver = new FakeWindowsAccountResolver();
        var service = CreateService(isAdmin: true, accountResolver: resolver);

        var result = await service.BindAccountAsync("AULA\\NoExiste", replaceExisting: true, CancellationToken.None);
        var binding = (await store.ReadAsync(CancellationToken.None)).Binding!;

        Assert.False(result.Configured);
        Assert.Equal(MasterBindingConfigurationStatus.WindowsAccountNotFound, result.Status);
        Assert.Equal("AULA\\MaestraA", binding.AccountDisplayName);
        Assert.Equal(FirstSid, binding.WindowsSid);
    }

    [Fact]
    public async Task BindAccountAsync_WhenBindingExistsWithoutReplace_IsRejected()
    {
        var service = CreateService(isAdmin: true);
        await service.BindCurrentUserAsync(replaceExisting: false, CancellationToken.None);

        var result = await service.BindAccountAsync("AULA\\MaestraB", replaceExisting: false, CancellationToken.None);
        var binding = (await CreateStore().ReadAsync(CancellationToken.None)).Binding!;

        Assert.False(result.Configured);
        Assert.Equal(MasterBindingConfigurationStatus.MasterBindingAlreadyConfigured, result.Status);
        Assert.Equal("AULA\\MaestraPrimaria", binding.AccountDisplayName);
    }

    [Fact]
    public async Task BindAccountAsync_WhenReplaceIsExplicit_RebindsAtomically()
    {
        var service = CreateService(isAdmin: true);
        await service.BindCurrentUserAsync(replaceExisting: false, CancellationToken.None);

        var result = await service.BindAccountAsync("AULA\\MaestraB", replaceExisting: true, CancellationToken.None);
        var binding = (await CreateStore().ReadAsync(CancellationToken.None)).Binding!;

        Assert.True(result.Configured);
        Assert.Equal("AULA\\MaestraB", binding.AccountDisplayName);
        Assert.Equal(SecondSid, binding.WindowsSid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private MasterBindingConfigurationService CreateService(
        bool isAdmin,
        IWindowsAccountResolver? accountResolver = null)
    {
        return new MasterBindingConfigurationService(
            CreateInstallationIdentityResolver(),
            CreateStore(),
            accountResolver ?? new FakeWindowsAccountResolver(
                new WindowsAccountIdentity(FirstSid, "AULA\\MaestraPrimaria"),
                new Dictionary<string, WindowsAccountIdentity>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AULA\\MaestraB"] = new WindowsAccountIdentity(SecondSid, "AULA\\MaestraB")
                }),
            new FakeAdministratorPrivilegeChecker(isAdmin),
            new FakeClock(FixedNowUtc));
    }

    private MasterBindingStore CreateStore()
    {
        return new MasterBindingStore(
            new MasterBindingStoreOptions(_dataDirectory),
            new NoOpMasterBindingFileSecurity());
    }

    private InstallationIdentityResolver CreateInstallationIdentityResolver()
    {
        return new InstallationIdentityResolver(
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            new FakeHardwareFingerprintProvider(),
            new FakeClock(FixedNowUtc));
    }

    private sealed class FakeWindowsAccountResolver : IWindowsAccountResolver
    {
        private readonly WindowsAccountIdentity? _currentUser;
        private readonly IReadOnlyDictionary<string, WindowsAccountIdentity> _accounts;

        public FakeWindowsAccountResolver()
            : this(null, new Dictionary<string, WindowsAccountIdentity>())
        {
        }

        public FakeWindowsAccountResolver(
            WindowsAccountIdentity? currentUser,
            IReadOnlyDictionary<string, WindowsAccountIdentity> accounts)
        {
            _currentUser = currentUser;
            _accounts = accounts;
        }

        public WindowsAccountResolution ResolveCurrentUser()
        {
            return _currentUser is null
                ? WindowsAccountResolution.NotFound("current user missing")
                : WindowsAccountResolution.Resolved(_currentUser);
        }

        public WindowsAccountResolution ResolveAccount(string accountName)
        {
            return _accounts.TryGetValue(accountName, out var identity)
                ? WindowsAccountResolution.Resolved(identity)
                : WindowsAccountResolution.NotFound("account missing");
        }
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

    private sealed class FakeHardwareFingerprintProvider : IHardwareFingerprintProvider
    {
        public Task<HardwareFingerprint> GetCurrentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(HardwareFingerprintFactory.FromRawValues(
                ["CPU SERIAL 1"],
                ["MOTHERBOARD SERIAL 1"],
                ["AA11BB22CC31"],
                ["DISK SERIAL 1"]));
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
