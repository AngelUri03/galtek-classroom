using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class RemoveManagedCredentialOperationHandlerTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private const string WindowsSid = "S-1-5-21-1000000000-1000000000-1000000000-1004";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.RemoveManagedCredential.Tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ManagedWindowsAccountId.Primary, ClassroomManagedWindowsAccountTypes.Primary)]
    [InlineData(ManagedWindowsAccountId.Secondary, ClassroomManagedWindowsAccountTypes.Secondary)]
    [InlineData(ManagedWindowsAccountId.Admin, ClassroomManagedWindowsAccountTypes.Admin)]
    public async Task HandleAsync_RemovesOnlyRequestedBoundRole(
        ManagedWindowsAccountId protoAccountId,
        string expectedAccountId)
    {
        await SaveBindingAsync(expectedAccountId);
        var credentials = new FakeCredentialStore();

        RemoteOperationHandlerResult result = await CreateHandler(credentials).HandleAsync(
            Request(protoAccountId),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(1, credentials.RemoveCalls);
        Assert.Equal(expectedAccountId, credentials.LastAccountId);
        Assert.Equal(InstallationId, credentials.LastInstallationId);
        Assert.DoesNotContain(WindowsSid, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_WhenBindingIsMissing_DoesNotTouchCredentialStore()
    {
        await SaveIdentityAsync();
        var credentials = new FakeCredentialStore();

        RemoteOperationHandlerResult result = await CreateHandler(credentials).HandleAsync(
            Request(ManagedWindowsAccountId.Primary),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.AccountNotConfigured, result.ErrorCode);
        Assert.Equal(0, credentials.RemoveCalls);
    }

    [Fact]
    public async Task HandleAsync_WhenCredentialIsAlreadyAbsent_IsIdempotentSuccess()
    {
        await SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Primary);
        var credentials = new FakeCredentialStore
        {
            NextResult = ManagedWindowsCredentialWriteResult.Failure(
                ManagedWindowsCredentialWriteStatus.NotFound,
                "managed-windows-credentials.dat",
                ManagedWindowsCredentialErrorCodes.ManagedCredentialNotConfigured,
                "missing")
        };

        RemoteOperationHandlerResult result = await CreateHandler(credentials).HandleAsync(
            Request(ManagedWindowsAccountId.Primary),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(NetworkOperationErrorCode.Unspecified, result.ErrorCode);
        Assert.Equal("Managed credential was already absent.", result.Message);
    }

    [Fact]
    public async Task HandleAsync_WhenStoreFails_ReturnsStructuredErrorWithoutStoreDetails()
    {
        await SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Primary);
        var credentials = new FakeCredentialStore
        {
            NextResult = ManagedWindowsCredentialWriteResult.Failure(
                ManagedWindowsCredentialWriteStatus.StoreInvalid,
                "managed-windows-credentials.dat",
                ManagedWindowsCredentialErrorCodes.ManagedCredentialStoreInvalid,
                "secret store detail")
        };

        RemoteOperationHandlerResult result = await CreateHandler(credentials).HandleAsync(
            Request(ManagedWindowsAccountId.Primary),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.ManagedCredentialStoreInvalid, result.ErrorCode);
        Assert.DoesNotContain("secret store detail", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatcher_DeduplicatesSameOperationId()
    {
        await SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Admin);
        var credentials = new FakeCredentialStore();
        var dispatcher = new RemoteOperationDispatcher(
            [CreateHandler(credentials)],
            new RemoteOperationOptions(),
            new FakeClock(FixedNow));
        OperationRequest request = Request(ManagedWindowsAccountId.Admin);

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(request, CancellationToken.None);
        RemoteOperationDispatchResult second = await dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, first.Result.Status);
        Assert.Equal(OperationExecutionStatus.Success, second.Result.Status);
        Assert.Equal(1, credentials.RemoveCalls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private RemoveManagedCredentialOperationHandler CreateHandler(FakeCredentialStore credentialStore)
    {
        return new RemoveManagedCredentialOperationHandler(
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            CreateBindingStore(),
            credentialStore,
            NullLogger<RemoveManagedCredentialOperationHandler>.Instance);
    }

    private ManagedWindowsAccountBindingStore CreateBindingStore()
    {
        return new ManagedWindowsAccountBindingStore(
            new ManagedWindowsAccountBindingStoreOptions(_dataDirectory),
            new NoOpManagedWindowsAccountBindingFileSecurity());
    }

    private async Task SaveBindingAsync(string accountId)
    {
        await SaveIdentityAsync();
        ManagedWindowsAccountBindingStoreWriteResult result = await CreateBindingStore().AddAsync(
            InstallationId,
            ManagedWindowsAccountBinding.Create(accountId, WindowsSid, "PC14\\Account", FixedNow),
            CancellationToken.None);
        Assert.True(result.Succeeded);
    }

    private async Task SaveIdentityAsync()
    {
        Directory.CreateDirectory(_dataDirectory);
        var identity = InstallationIdentity.Create(
            InstallationId,
            HardwareFingerprintFactory.FromRawValues(
                ["CPU"],
                ["BOARD"],
                ["AA11BB22CC33"],
                ["DISK"]),
            FixedNow);
        await new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory))
            .WriteNewAsync(identity, CancellationToken.None);
    }

    private static OperationRequest Request(ManagedWindowsAccountId accountId)
    {
        return new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.RemoveManagedCredential,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            RemoveManagedCredential = new RemoveManagedCredentialOperationParameters
            {
                AccountId = accountId
            }
        };
    }

    private sealed class FakeCredentialStore : IManagedWindowsCredentialStore
    {
        public string FilePath => "managed-windows-credentials.dat";
        public int RemoveCalls { get; private set; }
        public Guid LastInstallationId { get; private set; }
        public string? LastAccountId { get; private set; }
        public ManagedWindowsCredentialWriteResult NextResult { get; init; } =
            ManagedWindowsCredentialWriteResult.Success(
                ManagedWindowsCredentialWriteStatus.Removed,
                "managed-windows-credentials.dat");

        public Task<ManagedWindowsCredentialStatusResult> GetStatusAsync(
            Guid currentInstallationId, string accountId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagedWindowsCredentialWriteResult> AddAsync(
            Guid currentInstallationId, string accountId, ReadOnlyMemory<char> secret,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManagedWindowsCredentialWriteResult> AddUtf16LittleEndianAsync(
            Guid currentInstallationId, string accountId, ReadOnlyMemory<byte> secretUtf16LittleEndian,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManagedWindowsCredentialWriteResult> ReplaceAsync(
            Guid currentInstallationId, string accountId, ReadOnlyMemory<char> secret,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManagedWindowsCredentialWriteResult> ReplaceUtf16LittleEndianAsync(
            Guid currentInstallationId, string accountId, ReadOnlyMemory<byte> secretUtf16LittleEndian,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManagedWindowsCredentialWriteResult> RemoveAsync(
            Guid currentInstallationId,
            string accountId,
            CancellationToken cancellationToken)
        {
            RemoveCalls++;
            LastInstallationId = currentInstallationId;
            LastAccountId = accountId;
            return Task.FromResult(NextResult);
        }

        public Task<ManagedWindowsCredentialAcquireResult> AcquireAsync(
            Guid currentInstallationId, string accountId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagedWindowsCredentialAcquireResult> AcquireForWindowsSidAsync(
            Guid currentInstallationId, string accountId, string expectedWindowsSid,
            CancellationToken cancellationToken) => throw new NotSupportedException();
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
