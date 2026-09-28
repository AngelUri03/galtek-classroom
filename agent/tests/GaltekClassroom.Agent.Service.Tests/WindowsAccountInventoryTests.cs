using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.WindowsAccounts;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class WindowsAccountInventoryTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("a7cbb5ab-c21b-4dc3-9678-72bcfbd1c774");
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GaltekInventoryTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Handler_ReturnsMetadataMultipleAdminsBuiltInsAndManagedRolesWithoutSid()
    {
        await SaveIdentityAsync();
        var bindings = new FakeBindingStore([
            ManagedWindowsAccountBinding.Create("PRIMARY", "S-1-5-21-1-1001", @"PC14\Primaria", Now),
            ManagedWindowsAccountBinding.Create("SECONDARY", "S-1-5-21-1-1002", @"PC14\Secundaria", Now)
        ]);
        var source = new FakeSource([
            new("ADMIN-14", "Administración", true, true, false, "S-1-5-21-1-1000"),
            new("OtroAdmin", "Otro administrador", false, true, false, "S-1-5-21-1-1003"),
            new("PRIM", "Primaria", true, false, false, "S-1-5-21-1-1001"),
            new("SEC", "Secundaria", true, false, false, "S-1-5-21-1-1002"),
            new("Guest", "Guest", false, false, true, "S-1-5-21-1-501")
        ]);
        var handler = new GetWindowsAccountInventoryOperationHandler(
            IdentityStore(), bindings, source);

        RemoteOperationHandlerResult result = await handler.HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        WindowsAccountInventoryResult inventory = Assert.IsType<WindowsAccountInventoryResult>(result.WindowsAccountInventory);
        Assert.Equal(5, inventory.Accounts.Count);
        Assert.Equal(2, inventory.Accounts.Count(account => account.Administrator));
        Assert.False(inventory.Accounts.Single(account => account.AccountName == "OtroAdmin").Enabled);
        Assert.True(inventory.Accounts.Single(account => account.AccountName == "Guest").BuiltIn);
        Assert.Equal(ManagedWindowsAccountId.Primary, inventory.Accounts.Single(account => account.AccountName == "PRIM").ManagedRole);
        Assert.Equal(ManagedWindowsAccountId.Secondary, inventory.Accounts.Single(account => account.AccountName == "SEC").ManagedRole);
        Assert.Equal(ManagedWindowsAccountId.Unspecified, inventory.Accounts.Single(account => account.AccountName == "ADMIN-14").ManagedRole);
        Assert.DoesNotContain("S-1-5-21", inventory.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, source.ReadCalls);
        Assert.Equal(0, bindings.WriteCalls);
    }

    [Fact]
    public async Task Dispatcher_AttachesTypedInventoryAndRequiresCommercialLicense()
    {
        await SaveIdentityAsync();
        var handler = new GetWindowsAccountInventoryOperationHandler(
            IdentityStore(), new FakeBindingStore([]), new FakeSource([]));
        var dispatcher = new RemoteOperationDispatcher(
            [handler], new RemoteOperationOptions(), new FakeClock(Now));

        RemoteOperationDispatchResult dispatched = await dispatcher.DispatchAsync(Request(), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, dispatched.Result.Status);
        Assert.Equal(OperationResult.ResultDetailsOneofCase.WindowsAccountInventory, dispatched.Result.ResultDetailsCase);
        Assert.True(RemoteOperationLicensePolicy.Default.RequiresActiveCommercialLicense(
            NetworkOperationType.GetWindowsAccountInventory));
    }

    [Fact]
    public async Task Handler_RejectsParametersAndDoesNotReadWindows()
    {
        await SaveIdentityAsync();
        var source = new FakeSource([]);
        var handler = new GetWindowsAccountInventoryOperationHandler(
            IdentityStore(), new FakeBindingStore([]), source);
        OperationRequest request = Request();
        request.OpenUrl = new OpenUrlOperationParameters { Url = "https://example.test" };

        RemoteOperationHandlerResult result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.ProtocolViolation, result.ErrorCode);
        Assert.Equal(0, source.ReadCalls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private InstallationIdentityStore IdentityStore() =>
        new(new InstallationIdentityStoreOptions(_directory));

    private async Task SaveIdentityAsync()
    {
        Directory.CreateDirectory(_directory);
        var identity = InstallationIdentity.Create(
            InstallationId,
            HardwareFingerprintFactory.FromRawValues(["CPU"], ["BOARD"], ["AA11BB22CC33"], ["DISK"]),
            Now);
        await IdentityStore().WriteNewAsync(identity, CancellationToken.None);
    }

    private static OperationRequest Request() => new()
    {
        OperationId = Guid.NewGuid().ToString("D"),
        OperationType = NetworkOperationType.GetWindowsAccountInventory,
        TargetDeviceId = "device-1",
        ProtocolVersion = MasterConnectionConstants.ProtocolVersion
    };

    private sealed class FakeSource(IReadOnlyList<WindowsLocalAccountRecord> accounts) : IWindowsAccountInventorySource
    {
        public int ReadCalls { get; private set; }
        public IReadOnlyList<WindowsLocalAccountRecord> Read(CancellationToken cancellationToken)
        {
            ReadCalls++;
            return accounts;
        }
    }

    private sealed class FakeBindingStore(IReadOnlyList<ManagedWindowsAccountBinding> bindings)
        : IManagedWindowsAccountBindingStore
    {
        public string FilePath => "managed-windows-accounts.json";
        public int WriteCalls { get; private set; }
        private ManagedWindowsAccountBindingStoreReadResult ReadResult =>
            ManagedWindowsAccountBindingStoreReadResult.LoadedBindings(bindings, FilePath);
        public Task<ManagedWindowsAccountBindingStoreReadResult> LoadAsync(Guid id, CancellationToken token) => Task.FromResult(ReadResult);
        public Task<ManagedWindowsAccountBindingStoreReadResult> ListAsync(Guid id, CancellationToken token) => Task.FromResult(ReadResult);
        public Task<ManagedWindowsAccountBindingStoreReadResult> GetAsync(Guid id, string accountId, CancellationToken token) => Task.FromResult(ReadResult);
        public Task<ManagedWindowsAccountBindingStoreWriteResult> AddAsync(Guid id, ManagedWindowsAccountBinding binding, CancellationToken token) { WriteCalls++; throw new NotSupportedException(); }
        public Task<ManagedWindowsAccountBindingStoreWriteResult> ReplaceAsync(Guid id, ManagedWindowsAccountBinding binding, CancellationToken token) { WriteCalls++; throw new NotSupportedException(); }
        public Task<ManagedWindowsAccountBindingStoreWriteResult> RemoveAsync(Guid id, string accountId, CancellationToken token) { WriteCalls++; throw new NotSupportedException(); }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
