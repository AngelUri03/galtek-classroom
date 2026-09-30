using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.WindowsAccounts;
using GaltekClassroom.Agent.Service.WindowsSessions;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class ManagedAccountBindingMutationServiceTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("640706ad-81e7-4c32-8902-94ea27749c11");
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 18, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GaltekBindingV2", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BindPrimaryEligible_PersistsSidWithoutCredential()
    {
        TestContext context = await CreateAsync(Account("School", admin: false));

        RemoteOperationHandlerResult result = await context.Service.BindAsync(
            ManagedWindowsAccountId.Primary, "School", "device-14", CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        ManagedWindowsAccountBindingStoreReadResult stored = await context.Bindings.ListAsync(
            InstallationId, CancellationToken.None);
        ManagedWindowsAccountBinding binding = Assert.Single(stored.Bindings);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Primary, binding.AccountId);
        Assert.Equal("S-1-5-21-1000", binding.WindowsSid);
        Assert.Equal(0, context.Credentials.RemoveCalls);
    }

    [Fact]
    public async Task BindSecondaryEligible_PersistsSecondaryRole()
    {
        TestContext context = await CreateAsync(Account("SecondarySchool", admin: false));

        RemoteOperationHandlerResult result = await context.Service.BindAsync(
            ManagedWindowsAccountId.Secondary, "SecondarySchool", "device-14", CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        ManagedWindowsAccountBinding binding = Assert.Single((await context.Bindings.ListAsync(
            InstallationId, CancellationToken.None)).Bindings);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Secondary, binding.AccountId);
    }

    [Theory]
    [InlineData(ManagedWindowsAccountId.Primary, true, true, false, NetworkOperationErrorCode.WindowsAccountAdminNotAllowed)]
    [InlineData(ManagedWindowsAccountId.Secondary, true, true, false, NetworkOperationErrorCode.WindowsAccountAdminNotAllowed)]
    [InlineData(ManagedWindowsAccountId.Admin, false, true, false, NetworkOperationErrorCode.WindowsAccountAdminRequired)]
    [InlineData(ManagedWindowsAccountId.Admin, true, false, false, NetworkOperationErrorCode.WindowsAccountDisabled)]
    [InlineData(ManagedWindowsAccountId.Admin, true, true, true, NetworkOperationErrorCode.WindowsAccountBuiltIn)]
    public async Task Bind_RevalidatesLiveEligibility(
        ManagedWindowsAccountId role,
        bool administrator,
        bool enabled,
        bool builtIn,
        NetworkOperationErrorCode expected)
    {
        TestContext context = await CreateAsync(Account("Candidate", administrator, enabled, builtIn));

        RemoteOperationHandlerResult result = await context.Service.BindAsync(
            role, "Candidate", "device-14", CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(expected, result.ErrorCode);
        Assert.Empty((await context.Bindings.ListAsync(InstallationId, CancellationToken.None)).Bindings);
    }

    [Fact]
    public async Task BindAdminEligible_ThenRejectsSameSidForSecondRole()
    {
        TestContext context = await CreateAsync(Account("TeacherAdmin", admin: true));
        RemoteOperationHandlerResult first = await context.Service.BindAsync(
            ManagedWindowsAccountId.Admin, "TeacherAdmin", "device-14", CancellationToken.None);
        context.Inventory.Accounts = [Account("TeacherAdmin", admin: false)];

        RemoteOperationHandlerResult duplicate = await context.Service.BindAsync(
            ManagedWindowsAccountId.Primary, "TeacherAdmin", "device-14", CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, first.Status);
        Assert.Equal(NetworkOperationErrorCode.WindowsAccountAlreadyManaged, duplicate.ErrorCode);
    }

    [Fact]
    public async Task BindReplacesRoleAndDoesNotTransferPreviousCredential()
    {
        TestContext context = await CreateAsync(
            Account("First", admin: false, sid: "S-1-5-21-1000"),
            Account("Second", admin: false, sid: "S-1-5-21-2000"));
        await context.Service.BindAsync(
            ManagedWindowsAccountId.Primary, "First", "device-14", CancellationToken.None);

        RemoteOperationHandlerResult replacement = await context.Service.BindAsync(
            ManagedWindowsAccountId.Primary, "Second", "device-14", CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, replacement.Status);
        ManagedWindowsAccountBinding binding = Assert.Single((await context.Bindings.ListAsync(
            InstallationId, CancellationToken.None)).Bindings);
        Assert.Equal("Second", binding.AccountReference);
        Assert.Equal("S-1-5-21-2000", binding.WindowsSid);
        Assert.Equal(1, context.Credentials.RemoveCalls);
    }

    [Fact]
    public async Task BindDoesNotReplaceRoleWhileCurrentBindingSessionIsActive()
    {
        TestContext context = await CreateAsync(
            Account("First", admin: false, sid: "S-1-5-21-1000"),
            Account("Second", admin: false, sid: "S-1-5-21-2000"));
        await context.Service.BindAsync(
            ManagedWindowsAccountId.Primary, "First", "device-14", CancellationToken.None);
        context.Session.Observation = ConsoleSessionIdentityObservation.User(4, "S-1-5-21-1000");

        RemoteOperationHandlerResult result = await context.Service.BindAsync(
            ManagedWindowsAccountId.Primary, "Second", "device-14", CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.ManagedAccountSessionActive, result.ErrorCode);
        ManagedWindowsAccountBinding binding = Assert.Single((await context.Bindings.ListAsync(
            InstallationId, CancellationToken.None)).Bindings);
        Assert.Equal("First", binding.AccountReference);
        Assert.Equal(0, context.Credentials.RemoveCalls);
    }

    [Fact]
    public async Task BindFailsWhenInventoryBecameStale()
    {
        TestContext context = await CreateAsync(Account("Removed", admin: false));
        context.Inventory.Accounts = [];

        RemoteOperationHandlerResult result = await context.Service.BindAsync(
            ManagedWindowsAccountId.Primary, "Removed", "device-14", CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.AccountNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task UnbindActiveSession_IsBlockedWithoutLogoutOrMutation()
    {
        TestContext context = await CreateAsync(Account("School", admin: false));
        await context.Service.BindAsync(ManagedWindowsAccountId.Primary, "School", "device-14", CancellationToken.None);
        context.Session.Observation = ConsoleSessionIdentityObservation.User(3, "S-1-5-21-1000");

        RemoteOperationHandlerResult result = await context.Service.UnbindAsync(
            ManagedWindowsAccountId.Primary, "device-14", CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.ManagedAccountSessionActive, result.ErrorCode);
        Assert.Single((await context.Bindings.ListAsync(InstallationId, CancellationToken.None)).Bindings);
        Assert.Equal(0, context.Credentials.RemoveCalls);
    }

    [Fact]
    public async Task Unbind_RemovesBindingAndProtectedCredential()
    {
        TestContext context = await CreateAsync(Account("School", admin: false));
        await context.Service.BindAsync(ManagedWindowsAccountId.Primary, "School", "device-14", CancellationToken.None);
        context.Credentials.RemoveResult = ManagedWindowsCredentialWriteResult.Success(
            ManagedWindowsCredentialWriteStatus.Removed, context.Credentials.FilePath);

        RemoteOperationHandlerResult result = await context.Service.UnbindAsync(
            ManagedWindowsAccountId.Primary, "device-14", CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Empty((await context.Bindings.ListAsync(InstallationId, CancellationToken.None)).Bindings);
        Assert.Equal(1, context.Credentials.RemoveCalls);
    }

    [Fact]
    public async Task UnbindCredentialCleanupFailure_RestoresBinding()
    {
        TestContext context = await CreateAsync(Account("School", admin: false));
        await context.Service.BindAsync(ManagedWindowsAccountId.Primary, "School", "device-14", CancellationToken.None);
        context.Credentials.RemoveResult = ManagedWindowsCredentialWriteResult.Failure(
            ManagedWindowsCredentialWriteStatus.ProtectionFailed,
            context.Credentials.FilePath,
            "MANAGED_WINDOWS_CREDENTIAL_PROTECTION_FAILED",
            "cleanup failed");

        RemoteOperationHandlerResult result = await context.Service.UnbindAsync(
            ManagedWindowsAccountId.Primary, "device-14", CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.ManagedCredentialCleanupFailed, result.ErrorCode);
        Assert.Single((await context.Bindings.ListAsync(InstallationId, CancellationToken.None)).Bindings);
        Assert.Equal(1, context.Credentials.RemoveCalls);
    }

    private async Task<TestContext> CreateAsync(params WindowsLocalAccountRecord[] accounts)
    {
        Directory.CreateDirectory(_directory);
        var identityStore = new InstallationIdentityStore(new InstallationIdentityStoreOptions(_directory));
        await identityStore.WriteNewAsync(
            InstallationIdentity.Create(
                InstallationId,
                HardwareFingerprintFactory.FromRawValues(["CPU"], ["BOARD"], ["AABBCCDDEEFF"], ["DISK"]),
                Now),
            CancellationToken.None);
        var bindings = new ManagedWindowsAccountBindingStore(
            new ManagedWindowsAccountBindingStoreOptions(_directory),
            new NoOpManagedWindowsAccountBindingFileSecurity());
        var inventory = new FakeInventory { Accounts = accounts };
        var credentials = new FakeCredentialStore();
        var session = new FakeSessionResolver();
        var service = new ManagedAccountBindingMutationService(
            identityStore,
            bindings,
            credentials,
            inventory,
            session,
            new FakeClock(),
            NullLogger<ManagedAccountBindingMutationService>.Instance);
        return new TestContext(service, bindings, inventory, credentials, session);
    }

    private static WindowsLocalAccountRecord Account(
        string name,
        bool admin,
        bool enabled = true,
        bool builtIn = false,
        string sid = "S-1-5-21-1000")
    {
        return new WindowsLocalAccountRecord(name, name, enabled, admin, builtIn, sid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed record TestContext(
        ManagedAccountBindingMutationService Service,
        IManagedWindowsAccountBindingStore Bindings,
        FakeInventory Inventory,
        FakeCredentialStore Credentials,
        FakeSessionResolver Session);

    private sealed class FakeInventory : IWindowsAccountInventorySource
    {
        public IReadOnlyList<WindowsLocalAccountRecord> Accounts { get; set; } = [];
        public IReadOnlyList<WindowsLocalAccountRecord> Read(CancellationToken cancellationToken) => Accounts;
    }

    private sealed class FakeSessionResolver : IWindowsConsoleSessionResolver
    {
        public ConsoleSessionIdentityObservation Observation { get; set; } = ConsoleSessionIdentityObservation.NoSession(1);
        public Task<ConsoleSessionIdentityObservation> ObserveAsync(CancellationToken cancellationToken) => Task.FromResult(Observation);
    }

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeCredentialStore : IManagedWindowsCredentialStore
    {
        public string FilePath => Path.Combine("test", ManagedWindowsCredentialConstants.FileName);
        public int RemoveCalls { get; private set; }
        public ManagedWindowsCredentialWriteResult RemoveResult { get; set; } =
            ManagedWindowsCredentialWriteResult.Failure(
                ManagedWindowsCredentialWriteStatus.NotFound,
                "test",
                ManagedWindowsCredentialErrorCodes.ManagedCredentialNotConfigured,
                "not configured");
        public Task<ManagedWindowsCredentialWriteResult> RemoveAsync(Guid id, string role, CancellationToken token)
        {
            RemoveCalls++;
            return Task.FromResult(RemoveResult);
        }
        public Task<ManagedWindowsCredentialStatusResult> GetStatusAsync(Guid id, string role, CancellationToken token) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialWriteResult> AddAsync(Guid id, string role, ReadOnlyMemory<char> secret, CancellationToken token) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialWriteResult> AddUtf16LittleEndianAsync(Guid id, string role, ReadOnlyMemory<byte> secret, CancellationToken token) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialWriteResult> ReplaceAsync(Guid id, string role, ReadOnlyMemory<char> secret, CancellationToken token) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialWriteResult> ReplaceUtf16LittleEndianAsync(Guid id, string role, ReadOnlyMemory<byte> secret, CancellationToken token) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialAcquireResult> AcquireAsync(Guid id, string role, CancellationToken token) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialAcquireResult> AcquireForWindowsSidAsync(Guid id, string role, string sid, CancellationToken token) => throw new NotSupportedException();
    }
}
