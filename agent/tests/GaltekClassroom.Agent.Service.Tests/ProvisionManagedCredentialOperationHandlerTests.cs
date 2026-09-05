using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class ProvisionManagedCredentialOperationHandlerTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.ProvisionManagedCredential.Tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ManagedWindowsAccountId.Primary, "PRIMARY")]
    [InlineData(ManagedWindowsAccountId.Secondary, "SECONDARY")]
    public async Task HandleAsync_WhenAccountIdAndPasswordAreValid_ProvisionsCredential(
        ManagedWindowsAccountId protoAccountId,
        string expectedAccountId)
    {
        await SaveIdentityAsync();
        var store = new FakeCredentialStore();
        var handler = CreateHandler(store);

        RemoteOperationHandlerResult result = await handler.HandleAsync(
            Request(protoAccountId, Bytes("Clase 1!")),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(NetworkOperationErrorCode.Unspecified, result.ErrorCode);
        Assert.Equal(1, store.ReplaceUtf16LittleEndianCalls);
        Assert.Equal(expectedAccountId, store.LastAccountId);
        Assert.Equal(InstallationId, store.LastInstallationId);
        Assert.DoesNotContain("Clase 1!", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_WhenCredentialExists_ReplacesIt()
    {
        await SaveIdentityAsync();
        var store = new FakeCredentialStore();
        var handler = CreateHandler(store);

        await handler.HandleAsync(Request(ManagedWindowsAccountId.Primary, Bytes("old")), CancellationToken.None);
        await handler.HandleAsync(Request(ManagedWindowsAccountId.Primary, Bytes("new")), CancellationToken.None);

        Assert.Equal(2, store.ReplaceUtf16LittleEndianCalls);
        Assert.Equal(0, store.AddUtf16LittleEndianCalls);
        Assert.Equal(Bytes("new"), store.LastSecret);
    }

    [Theory]
    [InlineData(ManagedWindowsAccountId.Unspecified, new byte[] { 65, 0 })]
    [InlineData(ManagedWindowsAccountId.Primary, new byte[] { })]
    [InlineData(ManagedWindowsAccountId.Primary, new byte[] { 65 })]
    public async Task HandleAsync_WhenParametersAreInvalid_RejectsWithoutStoreCall(
        ManagedWindowsAccountId accountId,
        byte[] passwordUtf16Le)
    {
        await SaveIdentityAsync();
        var store = new FakeCredentialStore();
        var handler = CreateHandler(store);

        RemoteOperationHandlerResult result = await handler.HandleAsync(
            Request(accountId, passwordUtf16Le),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.ProtocolViolation, result.ErrorCode);
        Assert.Equal(0, store.ReplaceUtf16LittleEndianCalls);
    }

    [Fact]
    public async Task HandleAsync_WhenPasswordExceedsStoreMaximum_Rejects()
    {
        await SaveIdentityAsync();
        var tooLarge = new byte[(ManagedWindowsCredentialConstants.MaximumPasswordCharacters + 1) * 2];
        var handler = CreateHandler(new FakeCredentialStore());

        RemoteOperationHandlerResult result = await handler.HandleAsync(
            Request(ManagedWindowsAccountId.Primary, tooLarge),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.ProtocolViolation, result.ErrorCode);
    }

    [Theory]
    [InlineData(ManagedWindowsCredentialWriteStatus.AccountNotConfigured, NetworkOperationErrorCode.AccountNotConfigured)]
    [InlineData(ManagedWindowsCredentialWriteStatus.AccountNotFound, NetworkOperationErrorCode.AccountNotFound)]
    [InlineData(ManagedWindowsCredentialWriteStatus.BindingStoreInvalid, NetworkOperationErrorCode.ManagedAccountBindingsInvalid)]
    [InlineData(ManagedWindowsCredentialWriteStatus.StoreInvalid, NetworkOperationErrorCode.ManagedCredentialStoreInvalid)]
    [InlineData(ManagedWindowsCredentialWriteStatus.ProtectionFailed, NetworkOperationErrorCode.ManagedCredentialProtectionFailed)]
    [InlineData(ManagedWindowsCredentialWriteStatus.VerificationFailed, NetworkOperationErrorCode.ManagedCredentialStoreInvalid)]
    public async Task HandleAsync_MapsStoreFailuresToStructuredErrors(
        ManagedWindowsCredentialWriteStatus storeStatus,
        NetworkOperationErrorCode expectedError)
    {
        await SaveIdentityAsync();
        var store = new FakeCredentialStore
        {
            NextWriteResult = ManagedWindowsCredentialWriteResult.Failure(
                storeStatus,
                "managed-windows-credentials.dat",
                "IGNORED",
                "ignored")
        };
        var handler = CreateHandler(store);

        RemoteOperationHandlerResult result = await handler.HandleAsync(
            Request(ManagedWindowsAccountId.Primary, Bytes("secret")),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.DoesNotContain("secret", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_ZeroesControlledReceiveBufferAfterSuccess()
    {
        await SaveIdentityAsync();
        var store = new FakeCredentialStore();
        var handler = CreateHandler(store);

        await handler.HandleAsync(Request(ManagedWindowsAccountId.Primary, Bytes("secret")), CancellationToken.None);

        Assert.NotNull(store.LastSecretBackingArray);
        Assert.All(store.LastSecretBackingArray!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task HandleAsync_ZeroesControlledReceiveBufferAfterValidationFailure()
    {
        await SaveIdentityAsync();
        var store = new FakeCredentialStore();
        var handler = CreateHandler(store);

        await handler.HandleAsync(
            Request(ManagedWindowsAccountId.Primary, [65]),
            CancellationToken.None);

        Assert.Equal(0, store.ReplaceUtf16LittleEndianCalls);
    }

    [Fact]
    public async Task HandleAsync_ZeroesControlledReceiveBufferAfterStoreFailure()
    {
        await SaveIdentityAsync();
        var store = new FakeCredentialStore
        {
            NextWriteResult = ManagedWindowsCredentialWriteResult.Failure(
                ManagedWindowsCredentialWriteStatus.ProtectionFailed,
                "managed-windows-credentials.dat",
                ManagedWindowsCredentialErrorCodes.ManagedCredentialProtectionFailed,
                "failed")
        };
        var handler = CreateHandler(store);

        await handler.HandleAsync(Request(ManagedWindowsAccountId.Primary, Bytes("secret")), CancellationToken.None);

        Assert.NotNull(store.LastSecretBackingArray);
        Assert.All(store.LastSecretBackingArray!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task HandleAsync_DoesNotCompleteBeforeStoreReportsDurableSuccess()
    {
        await SaveIdentityAsync();
        var store = new FakeCredentialStore
        {
            PendingWrite = new TaskCompletionSource<ManagedWindowsCredentialWriteResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var handler = CreateHandler(store);

        Task<RemoteOperationHandlerResult> operation = handler.HandleAsync(
            Request(ManagedWindowsAccountId.Primary, Bytes("secret")),
            CancellationToken.None);

        Assert.False(operation.IsCompleted);
        store.PendingWrite.SetResult(ManagedWindowsCredentialWriteResult.Success(
            ManagedWindowsCredentialWriteStatus.Configured,
            "managed-windows-credentials.dat"));
        RemoteOperationHandlerResult result = await operation;

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
    }

    [Fact]
    public void Handler_DoesNotRetainPasswordFields()
    {
        var fields = typeof(ProvisionManagedCredentialOperationHandler)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(fields, field =>
            field.FieldType == typeof(byte[])
            || field.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
            || field.Name.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DispatchResult_DoesNotContainPassword()
    {
        await SaveIdentityAsync();
        var password = Bytes("secret-value");
        var handler = CreateHandler(new FakeCredentialStore());
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new FakeClock(FixedNow));

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            Request(ManagedWindowsAccountId.Primary, password),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, dispatch.Result.Status);
        Assert.Equal(OperationResult.ResultDetailsOneofCase.None, dispatch.Result.ResultDetailsCase);
        Assert.DoesNotContain("secret-value", dispatch.Result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(password), dispatch.Result.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private ProvisionManagedCredentialOperationHandler CreateHandler(FakeCredentialStore store)
    {
        return new ProvisionManagedCredentialOperationHandler(
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            store,
            NullLogger<ProvisionManagedCredentialOperationHandler>.Instance);
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

    private static OperationRequest Request(
        ManagedWindowsAccountId accountId,
        byte[] passwordUtf16Le)
    {
        return new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.ProvisionManagedCredential,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            ProvisionManagedCredential = new ProvisionManagedCredentialOperationParameters
            {
                AccountId = accountId,
                PasswordUtf16Le = ByteString.CopyFrom(passwordUtf16Le)
            }
        };
    }

    private static byte[] Bytes(string value)
    {
        return Encoding.Unicode.GetBytes(value);
    }

    private sealed class FakeCredentialStore : IManagedWindowsCredentialStore
    {
        public string FilePath => "managed-windows-credentials.dat";
        public int AddUtf16LittleEndianCalls { get; private set; }
        public int ReplaceUtf16LittleEndianCalls { get; private set; }
        public Guid LastInstallationId { get; private set; }
        public string? LastAccountId { get; private set; }
        public byte[]? LastSecret { get; private set; }
        public byte[]? LastSecretBackingArray { get; private set; }
        public TaskCompletionSource<ManagedWindowsCredentialWriteResult>? PendingWrite { get; init; }
        public ManagedWindowsCredentialWriteResult NextWriteResult { get; init; } =
            ManagedWindowsCredentialWriteResult.Success(
                ManagedWindowsCredentialWriteStatus.Configured,
                "managed-windows-credentials.dat");

        public Task<ManagedWindowsCredentialStatusResult> GetStatusAsync(
            Guid currentInstallationId,
            string accountId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ManagedWindowsCredentialWriteResult> AddAsync(
            Guid currentInstallationId,
            string accountId,
            ReadOnlyMemory<char> secret,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ManagedWindowsCredentialWriteResult> AddUtf16LittleEndianAsync(
            Guid currentInstallationId,
            string accountId,
            ReadOnlyMemory<byte> secretUtf16LittleEndian,
            CancellationToken cancellationToken)
        {
            AddUtf16LittleEndianCalls++;
            return CaptureAndReturn(currentInstallationId, accountId, secretUtf16LittleEndian);
        }

        public Task<ManagedWindowsCredentialWriteResult> ReplaceAsync(
            Guid currentInstallationId,
            string accountId,
            ReadOnlyMemory<char> secret,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ManagedWindowsCredentialWriteResult> ReplaceUtf16LittleEndianAsync(
            Guid currentInstallationId,
            string accountId,
            ReadOnlyMemory<byte> secretUtf16LittleEndian,
            CancellationToken cancellationToken)
        {
            ReplaceUtf16LittleEndianCalls++;
            return CaptureAndReturn(currentInstallationId, accountId, secretUtf16LittleEndian);
        }

        public Task<ManagedWindowsCredentialWriteResult> RemoveAsync(
            Guid currentInstallationId,
            string accountId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ManagedWindowsCredentialAcquireResult> AcquireAsync(
            Guid currentInstallationId,
            string accountId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ManagedWindowsCredentialAcquireResult> AcquireForWindowsSidAsync(
            Guid currentInstallationId,
            string accountId,
            string expectedWindowsSid,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        private Task<ManagedWindowsCredentialWriteResult> CaptureAndReturn(
            Guid currentInstallationId,
            string accountId,
            ReadOnlyMemory<byte> secretUtf16LittleEndian)
        {
            LastInstallationId = currentInstallationId;
            LastAccountId = accountId;
            LastSecret = secretUtf16LittleEndian.ToArray();
            if (MemoryMarshal.TryGetArray(secretUtf16LittleEndian, out ArraySegment<byte> segment))
            {
                LastSecretBackingArray = segment.Array;
            }

            return PendingWrite?.Task ?? Task.FromResult(NextWriteResult);
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
