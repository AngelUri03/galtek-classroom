using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class ManagedWindowsAccountBindingConfigurationServiceTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private const string PrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-1004";
    private const string SecondarySid = "S-1-5-21-1000000000-1000000000-1000000000-1005";
    private const string NewPrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-2004";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.ManagedWindowsAccountConfig.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ListAsync_WhenNoBindingsExist_ReturnsBothSlotsNotConfigured()
    {
        var result = await CreateService(isAdmin: false).ListAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Accounts.Count);
        Assert.All(result.Accounts, account =>
        {
            Assert.False(account.Configured);
            Assert.False(account.CredentialConfigured);
            Assert.Equal(ClassroomManagedWindowsAccountStatuses.NotConfigured, account.Status);
        });
        Assert.False(File.Exists(BindingFilePath()));
    }

    [Fact]
    public async Task BindAsync_WhenNotElevated_ReturnsAdministratorRequiredAndDoesNotWrite()
    {
        var result = await CreateService(isAdmin: false).BindAsync(
            ClassroomManagedWindowsAccountTypes.Primary,
            "Primaria",
            replaceExisting: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ManagedWindowsAccountBindingConfigurationStatus.AdministratorRequired, result.Status);
        Assert.False(File.Exists(BindingFilePath()));
    }

    [Fact]
    public async Task BindAsync_WhenShortLocalNameIsProvided_ResolvesMachineQualifiedName()
    {
        var resolver = new FakeWindowsAccountResolver();
        resolver.Accounts[$"{Environment.MachineName}\\Primaria"] = User(PrimarySid, "PC23\\Primaria");
        var service = CreateService(isAdmin: true, resolver);

        var result = await service.BindAsync(
            ClassroomManagedWindowsAccountTypes.Primary,
            "Primaria",
            replaceExisting: false,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains($"{Environment.MachineName}\\Primaria", resolver.ResolveAccountInputs);
    }

    [Fact]
    public async Task BindAsync_WhenUserAccountIsValid_PersistsCanonicalReference()
    {
        var service = CreateService(isAdmin: true);

        var result = await service.BindAsync(
            ClassroomManagedWindowsAccountTypes.Primary,
            "PC23\\Primaria",
            replaceExisting: false,
            CancellationToken.None);
        var stored = (await CreateStore().LoadAsync(await CurrentInstallationIdAsync(), CancellationToken.None)).Bindings.Single();

        Assert.True(result.Succeeded);
        Assert.Equal("PC23\\PrimariaCanonica", stored.AccountReference);
        Assert.Equal(PrimarySid, stored.WindowsSid);
    }

    [Theory]
    [InlineData(WindowsAccountSidNameUse.Group)]
    [InlineData(WindowsAccountSidNameUse.Alias)]
    [InlineData(WindowsAccountSidNameUse.WellKnownGroup)]
    [InlineData(WindowsAccountSidNameUse.Domain)]
    [InlineData(WindowsAccountSidNameUse.Computer)]
    [InlineData(WindowsAccountSidNameUse.Unknown)]
    public async Task BindAsync_WhenResolvedAccountIsNotUser_IsRejected(
        WindowsAccountSidNameUse sidNameUse)
    {
        var resolver = new FakeWindowsAccountResolver();
        resolver.Accounts["PC23\\Primaria"] = new WindowsAccountIdentity(
            PrimarySid,
            "PC23\\Primaria",
            sidNameUse);

        var result = await CreateService(isAdmin: true, resolver).BindAsync(
            ClassroomManagedWindowsAccountTypes.Primary,
            "PC23\\Primaria",
            replaceExisting: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ManagedWindowsAccountBindingConfigurationStatus.WindowsAccountNotFound, result.Status);
        Assert.False(File.Exists(BindingFilePath()));
    }

    [Fact]
    public async Task BindAsync_WhenUnknownAccount_IsRejected()
    {
        var result = await CreateService(isAdmin: true, new FakeWindowsAccountResolver()).BindAsync(
            ClassroomManagedWindowsAccountTypes.Primary,
            "PC23\\NoExiste",
            replaceExisting: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ManagedWindowsAccountBindingConfigurationStatus.WindowsAccountNotFound, result.Status);
    }

    [Fact]
    public async Task BindAsync_WhenSlotAlreadyExistsWithoutReplace_IsRejected()
    {
        var service = CreateService(isAdmin: true);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Primary, "PC23\\Primaria", false, CancellationToken.None);

        var result = await service.BindAsync(
            ClassroomManagedWindowsAccountTypes.Primary,
            "PC23\\PrimariaNueva",
            replaceExisting: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingAlreadyExists, result.Status);
    }

    [Fact]
    public async Task BindAsync_WhenReplaceIsExplicit_ChangesPrimaryAndPreservesSecondary()
    {
        var service = CreateService(isAdmin: true);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Primary, "PC23\\Primaria", false, CancellationToken.None);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Secondary, "PC23\\Secundaria", false, CancellationToken.None);

        var result = await service.BindAsync(
            ClassroomManagedWindowsAccountTypes.Primary,
            "PC23\\PrimariaNueva",
            replaceExisting: true,
            CancellationToken.None);
        var reopened = await CreateStore().LoadAsync(await CurrentInstallationIdAsync(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains(reopened.Bindings, binding => binding.AccountId == ClassroomManagedWindowsAccountTypes.Primary
            && binding.WindowsSid == NewPrimarySid);
        Assert.Contains(reopened.Bindings, binding => binding.AccountId == ClassroomManagedWindowsAccountTypes.Secondary
            && binding.WindowsSid == SecondarySid);
    }

    [Fact]
    public async Task BindAsync_WhenOtherSlotUsesSameSid_ReturnsConflict()
    {
        var resolver = CreateDefaultResolver();
        resolver.Accounts["PC23\\SecundariaMisma"] = User(PrimarySid, "PC23\\SecundariaMisma");
        var service = CreateService(isAdmin: true, resolver);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Primary, "PC23\\Primaria", false, CancellationToken.None);

        var result = await service.BindAsync(
            ClassroomManagedWindowsAccountTypes.Secondary,
            "PC23\\SecundariaMisma",
            replaceExisting: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingConflict, result.Status);
    }

    [Fact]
    public async Task BindAsync_WhenAccountIdIsInvalid_IsRejected()
    {
        var result = await CreateService(isAdmin: true).BindAsync(
            "TERTIARY",
            "PC23\\Primaria",
            replaceExisting: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingInvalid, result.Status);
    }

    [Fact]
    public async Task RemoveAsync_WhenBindingExists_RemovesOnlyRequestedSlot()
    {
        var service = CreateService(isAdmin: true);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Primary, "PC23\\Primaria", false, CancellationToken.None);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Secondary, "PC23\\Secundaria", false, CancellationToken.None);

        var result = await service.RemoveAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);
        var reopened = await CreateStore().LoadAsync(await CurrentInstallationIdAsync(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(reopened.Bindings);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Secondary, reopened.Bindings[0].AccountId);
    }

    [Fact]
    public async Task RemoveAsync_WhenMissing_ReturnsNotFoundAndDoesNotTouchWindowsAccount()
    {
        var resolver = CreateDefaultResolver();

        var result = await CreateService(isAdmin: true, resolver).RemoveAsync(
            ClassroomManagedWindowsAccountTypes.Primary,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingNotFound, result.Status);
        Assert.Empty(resolver.ResolveAccountInputs);
        Assert.Empty(resolver.ResolveSidInputs);
    }

    [Fact]
    public async Task ListAsync_WhenAccountWasRenamedButSidStillResolves_UsesCanonicalNameWithoutWriting()
    {
        var resolver = CreateDefaultResolver();
        var service = CreateService(isAdmin: true, resolver);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Primary, "PC23\\Primaria", false, CancellationToken.None);
        var before = await File.ReadAllTextAsync(BindingFilePath());
        resolver.Sids[PrimarySid] = User(PrimarySid, "PC23\\Primaria2026");

        var result = await service.ListAsync(CancellationToken.None);
        var after = await File.ReadAllTextAsync(BindingFilePath());

        Assert.True(result.Succeeded);
        Assert.Equal("PC23\\Primaria2026", result.Accounts.Single(account => account.Configured).AccountReference);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ListAsync_WhenSidNoLongerResolves_ReturnsAccountNotFound()
    {
        var resolver = CreateDefaultResolver();
        var service = CreateService(isAdmin: true, resolver);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Primary, "PC23\\Primaria", false, CancellationToken.None);
        resolver.Sids.Remove(PrimarySid);

        var result = await service.ListAsync(CancellationToken.None);
        var primary = result.Accounts.Single(account => account.AccountId == ClassroomManagedWindowsAccountTypes.Primary);

        Assert.True(result.Succeeded);
        Assert.True(primary.Configured);
        Assert.False(primary.CredentialConfigured);
        Assert.Equal(ClassroomManagedWindowsAccountStatuses.AccountNotFound, primary.Status);
    }

    [Fact]
    public async Task ListAsync_WhenSameNameIsRecreatedWithNewSid_DoesNotAdoptNewSid()
    {
        var resolver = CreateDefaultResolver();
        var service = CreateService(isAdmin: true, resolver);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Primary, "PC23\\Primaria", false, CancellationToken.None);
        resolver.Sids.Remove(PrimarySid);
        resolver.Accounts["PC23\\Primaria"] = User(NewPrimarySid, "PC23\\Primaria");

        var result = await service.ListAsync(CancellationToken.None);
        var stored = (await CreateStore().LoadAsync(await CurrentInstallationIdAsync(), CancellationToken.None)).Bindings.Single();

        Assert.Equal(ClassroomManagedWindowsAccountStatuses.AccountNotFound, result.Accounts[0].Status);
        Assert.Equal(PrimarySid, stored.WindowsSid);
    }

    [Fact]
    public async Task BindAsync_WhenReplaceAfterRecreateIsExplicit_AdoptsNewSid()
    {
        var resolver = CreateDefaultResolver();
        var service = CreateService(isAdmin: true, resolver);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Primary, "PC23\\Primaria", false, CancellationToken.None);
        resolver.Sids.Remove(PrimarySid);
        resolver.Accounts["PC23\\Primaria"] = User(NewPrimarySid, "PC23\\Primaria");
        resolver.Sids[NewPrimarySid] = User(NewPrimarySid, "PC23\\Primaria");

        var result = await service.BindAsync(
            ClassroomManagedWindowsAccountTypes.Primary,
            "PC23\\Primaria",
            replaceExisting: true,
            CancellationToken.None);
        var stored = (await CreateStore().LoadAsync(await CurrentInstallationIdAsync(), CancellationToken.None)).Bindings.Single();

        Assert.True(result.Succeeded);
        Assert.Equal(NewPrimarySid, stored.WindowsSid);
    }

    [Fact]
    public async Task ListAsync_WhenUserExists_ReturnsCredentialNotConfiguredAndNeverReady()
    {
        var service = CreateService(isAdmin: true);
        await service.BindAsync(ClassroomManagedWindowsAccountTypes.Primary, "PC23\\Primaria", false, CancellationToken.None);

        var result = await service.ListAsync(CancellationToken.None);
        var primary = result.Accounts.Single(account => account.AccountId == ClassroomManagedWindowsAccountTypes.Primary);

        Assert.Equal(ClassroomManagedWindowsAccountStatuses.CredentialNotConfigured, primary.Status);
        Assert.False(primary.CredentialConfigured);
        Assert.DoesNotContain(result.Accounts, account => account.Status == ClassroomManagedWindowsAccountStatuses.Ready);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private ManagedWindowsAccountBindingConfigurationService CreateService(
        bool isAdmin,
        FakeWindowsAccountResolver? resolver = null)
    {
        return new ManagedWindowsAccountBindingConfigurationService(
            CreateInstallationIdentityResolver(),
            CreateStore(),
            resolver ?? CreateDefaultResolver(),
            new FakeAdministratorPrivilegeChecker(isAdmin),
            new FakeClock(FixedNowUtc));
    }

    private ManagedWindowsAccountBindingStore CreateStore()
    {
        return new ManagedWindowsAccountBindingStore(
            new ManagedWindowsAccountBindingStoreOptions(_dataDirectory),
            new NoOpManagedWindowsAccountBindingFileSecurity());
    }

    private InstallationIdentityResolver CreateInstallationIdentityResolver()
    {
        return new InstallationIdentityResolver(
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            new FakeHardwareFingerprintProvider(),
            new FakeClock(FixedNowUtc));
    }

    private string BindingFilePath()
    {
        return Path.Combine(_dataDirectory, ManagedWindowsAccountBindingConstants.FileName);
    }

    private async Task<Guid> CurrentInstallationIdAsync()
    {
        var identity = await new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory))
            .ReadAsync(CancellationToken.None);
        return identity.Identity!.InstallationId;
    }

    private static FakeWindowsAccountResolver CreateDefaultResolver()
    {
        var resolver = new FakeWindowsAccountResolver();
        resolver.Accounts["PC23\\Primaria"] = User(PrimarySid, "PC23\\PrimariaCanonica");
        resolver.Accounts["PC23\\Secundaria"] = User(SecondarySid, "PC23\\Secundaria");
        resolver.Accounts["PC23\\PrimariaNueva"] = User(NewPrimarySid, "PC23\\PrimariaNueva");
        resolver.Sids[PrimarySid] = User(PrimarySid, "PC23\\PrimariaCanonica");
        resolver.Sids[SecondarySid] = User(SecondarySid, "PC23\\Secundaria");
        resolver.Sids[NewPrimarySid] = User(NewPrimarySid, "PC23\\PrimariaNueva");
        return resolver;
    }

    private static WindowsAccountIdentity User(string sid, string accountReference)
    {
        return new WindowsAccountIdentity(sid, accountReference, WindowsAccountSidNameUse.User);
    }

    private sealed class FakeWindowsAccountResolver : IWindowsAccountResolver
    {
        public Dictionary<string, WindowsAccountIdentity> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, WindowsAccountIdentity> Sids { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ResolveAccountInputs { get; } = [];
        public List<string> ResolveSidInputs { get; } = [];

        public WindowsAccountResolution ResolveCurrentUser()
        {
            return WindowsAccountResolution.NotFound("current user missing");
        }

        public WindowsAccountResolution ResolveAccount(string accountName)
        {
            ResolveAccountInputs.Add(accountName);
            return Accounts.TryGetValue(accountName, out var identity)
                ? WindowsAccountResolution.Resolved(identity)
                : WindowsAccountResolution.NotFound("account missing");
        }

        public WindowsAccountResolution ResolveSid(string windowsSid)
        {
            ResolveSidInputs.Add(windowsSid);
            return Sids.TryGetValue(windowsSid, out var identity)
                ? WindowsAccountResolution.Resolved(identity)
                : WindowsAccountResolution.NotFound("sid missing");
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
