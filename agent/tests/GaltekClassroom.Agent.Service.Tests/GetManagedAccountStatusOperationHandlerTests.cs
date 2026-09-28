using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class GetManagedAccountStatusOperationHandlerTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.ManagedAccountStatus.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HandleAsync_ReturnsBothSlotsWithoutSecretsOrAcquire()
    {
        await SaveIdentityAsync();
        var bindingStore = new FakeBindingStore([
            ManagedWindowsAccountBinding.Create(
                ClassroomManagedWindowsAccountTypes.Primary,
                "S-1-5-21-1007",
                @"PC14\Primaria",
                FixedNow)
        ]);
        var credentialStore = new FakeCredentialStore
        {
            PrimaryStatus = ManagedWindowsCredentialStatusResult.Success("managed-windows-credentials.dat")
        };
        var resolver = new FakeWindowsAccountResolver();
        resolver.BySid["S-1-5-21-1007"] = WindowsAccountResolution.Resolved(
            new WindowsAccountIdentity("S-1-5-21-1007", @"PC14\PrimariaCanonica"));
        var handler = CreateHandler(bindingStore, credentialStore, resolver);

        RemoteOperationHandlerResult result = await handler.HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.NotNull(result.ManagedAccountStatus);
        Assert.Equal(3, result.ManagedAccountStatus!.Accounts.Count);
        ManagedAccountStatus primary = result.ManagedAccountStatus.Accounts[0];
        ManagedAccountStatus secondary = result.ManagedAccountStatus.Accounts[1];
        Assert.Equal(ManagedWindowsAccountId.Primary, primary.AccountId);
        Assert.True(primary.Configured);
        Assert.True(primary.CredentialConfigured);
        Assert.Equal(ManagedAccountCredentialStatus.Ready, primary.CredentialStatus);
        Assert.Equal(@"PC14\PrimariaCanonica", primary.WindowsAccountName);
        Assert.Equal(ManagedWindowsAccountId.Secondary, secondary.AccountId);
        Assert.False(secondary.Configured);
        Assert.False(secondary.CredentialConfigured);
        Assert.Equal(ManagedAccountCredentialStatus.NotConfigured, secondary.CredentialStatus);
        ManagedAccountStatus admin = result.ManagedAccountStatus.Accounts[2];
        Assert.Equal(ManagedWindowsAccountId.Admin, admin.AccountId);
        Assert.False(admin.Configured);
        Assert.Equal(1, credentialStore.GetStatusCalls);
        Assert.Equal(0, credentialStore.AcquireCalls);
        Assert.DoesNotContain("Password", result.ManagedAccountStatus.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("S-1-5-21", result.ManagedAccountStatus.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_WhenBindingStoreIsInvalid_FailsClosed()
    {
        await SaveIdentityAsync();
        var bindingStore = new FakeBindingStore([])
        {
            ReadResult = ManagedWindowsAccountBindingStoreReadResult.Invalid(
                "managed-windows-accounts.json",
                "invalid")
        };
        var credentialStore = new FakeCredentialStore();
        var handler = CreateHandler(bindingStore, credentialStore, new FakeWindowsAccountResolver());

        RemoteOperationHandlerResult result = await handler.HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.ManagedAccountBindingsInvalid, result.ErrorCode);
        Assert.Equal(0, credentialStore.GetStatusCalls);
        Assert.Equal(0, credentialStore.AcquireCalls);
    }

    [Fact]
    public async Task Dispatcher_AttachesManagedAccountStatusDetails()
    {
        await SaveIdentityAsync();
        var bindingStore = new FakeBindingStore([]);
        var credentialStore = new FakeCredentialStore();
        var handler = CreateHandler(bindingStore, credentialStore, new FakeWindowsAccountResolver());
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new FakeClock(FixedNow));

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(Request(), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, dispatch.Result.Status);
        Assert.Equal(OperationResult.ResultDetailsOneofCase.ManagedAccountStatus, dispatch.Result.ResultDetailsCase);
        Assert.Equal(3, dispatch.Result.ManagedAccountStatus.Accounts.Count);
        Assert.DoesNotContain("protectedData", dispatch.Result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CredentialId", dispatch.Result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private GetManagedAccountStatusOperationHandler CreateHandler(
        FakeBindingStore bindingStore,
        FakeCredentialStore credentialStore,
        FakeWindowsAccountResolver resolver)
    {
        return new GetManagedAccountStatusOperationHandler(
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            bindingStore,
            credentialStore,
            resolver);
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

    private static OperationRequest Request()
    {
        return new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.GetManagedAccountStatus,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion
        };
    }

    private sealed class FakeBindingStore : IManagedWindowsAccountBindingStore
    {
        private readonly IReadOnlyList<ManagedWindowsAccountBinding> _bindings;

        public FakeBindingStore(IReadOnlyList<ManagedWindowsAccountBinding> bindings)
        {
            _bindings = bindings;
            ReadResult = ManagedWindowsAccountBindingStoreReadResult.LoadedBindings(
                bindings,
                "managed-windows-accounts.json");
        }

        public string FilePath => "managed-windows-accounts.json";
        public ManagedWindowsAccountBindingStoreReadResult ReadResult { get; init; }

        public Task<ManagedWindowsAccountBindingStoreReadResult> LoadAsync(
            Guid currentInstallationId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ReadResult);
        }

        public Task<ManagedWindowsAccountBindingStoreReadResult> ListAsync(
            Guid currentInstallationId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ReadResult);
        }

        public Task<ManagedWindowsAccountBindingStoreReadResult> GetAsync(
            Guid currentInstallationId,
            string accountId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ManagedWindowsAccountBindingStoreReadResult.LoadedBindings(
                _bindings.Where(binding => binding.AccountId == accountId).ToList(),
                FilePath));
        }

        public Task<ManagedWindowsAccountBindingStoreWriteResult> AddAsync(
            Guid currentInstallationId,
            ManagedWindowsAccountBinding binding,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ManagedWindowsAccountBindingStoreWriteResult> ReplaceAsync(
            Guid currentInstallationId,
            ManagedWindowsAccountBinding binding,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ManagedWindowsAccountBindingStoreWriteResult> RemoveAsync(
            Guid currentInstallationId,
            string accountId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeCredentialStore : IManagedWindowsCredentialStore
    {
        public string FilePath => "managed-windows-credentials.dat";
        public int GetStatusCalls { get; private set; }
        public int AcquireCalls { get; private set; }
        public ManagedWindowsCredentialStatusResult PrimaryStatus { get; init; } =
            ManagedWindowsCredentialStatusResult.Failure(
                ManagedWindowsCredentialStatus.CredentialNotConfigured,
                "managed-windows-credentials.dat",
                ManagedWindowsCredentialErrorCodes.ManagedCredentialNotConfigured,
                "not configured");

        public Task<ManagedWindowsCredentialStatusResult> GetStatusAsync(
            Guid currentInstallationId,
            string accountId,
            CancellationToken cancellationToken)
        {
            GetStatusCalls++;
            return Task.FromResult(PrimaryStatus);
        }

        public Task<ManagedWindowsCredentialWriteResult> AddAsync(Guid currentInstallationId, string accountId, ReadOnlyMemory<char> secret, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialWriteResult> AddUtf16LittleEndianAsync(Guid currentInstallationId, string accountId, ReadOnlyMemory<byte> secretUtf16LittleEndian, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialWriteResult> ReplaceAsync(Guid currentInstallationId, string accountId, ReadOnlyMemory<char> secret, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialWriteResult> ReplaceUtf16LittleEndianAsync(Guid currentInstallationId, string accountId, ReadOnlyMemory<byte> secretUtf16LittleEndian, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ManagedWindowsCredentialWriteResult> RemoveAsync(Guid currentInstallationId, string accountId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManagedWindowsCredentialAcquireResult> AcquireAsync(Guid currentInstallationId, string accountId, CancellationToken cancellationToken)
        {
            AcquireCalls++;
            throw new NotSupportedException();
        }

        public Task<ManagedWindowsCredentialAcquireResult> AcquireForWindowsSidAsync(Guid currentInstallationId, string accountId, string expectedWindowsSid, CancellationToken cancellationToken)
        {
            AcquireCalls++;
            throw new NotSupportedException();
        }
    }

    private sealed class FakeWindowsAccountResolver : IWindowsAccountResolver
    {
        public Dictionary<string, WindowsAccountResolution> BySid { get; } = new(StringComparer.OrdinalIgnoreCase);

        public WindowsAccountResolution ResolveCurrentUser() => WindowsAccountResolution.NotFound("not used");

        public WindowsAccountResolution ResolveAccount(string accountName) => WindowsAccountResolution.NotFound("not used");

        public WindowsAccountResolution ResolveSid(string windowsSid)
        {
            return BySid.TryGetValue(windowsSid, out WindowsAccountResolution? resolution)
                ? resolution
                : WindowsAccountResolution.NotFound("not found");
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
