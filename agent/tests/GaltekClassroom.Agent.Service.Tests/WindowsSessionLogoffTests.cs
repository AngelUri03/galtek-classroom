using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.WindowsSessions;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class WindowsSessionLogoffTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private const string PrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-1004";
    private const string SecondarySid = "S-1-5-21-1000000000-1000000000-1000000000-1005";
    private const string OtherSid = "S-1-5-21-1000000000-1000000000-1000000000-1006";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.WindowsSessionLogoff.Tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ManagedWindowsAccountId.Primary, ClassroomManagedWindowsAccountTypes.Primary)]
    [InlineData(ManagedWindowsAccountId.Secondary, ClassroomManagedWindowsAccountTypes.Secondary)]
    public async Task Handler_AcceptsPrimaryAndSecondaryOnly(
        ManagedWindowsAccountId networkAccountId,
        string expectedAccountId)
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);
        var logoff = new RecordingLogoffController();
        var handler = CreateHandler(
            new SequenceResolver([
                ObservationFor(expectedAccountId, 4),
                ObservationFor(expectedAccountId, 4)
            ]),
            logoff);

        RemoteOperationHandlerResult result =
            await handler.HandleAsync(CreateRequest(networkAccountId), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(1, logoff.Calls);
        Assert.Equal(4u, logoff.SessionIds.Single());
    }

    [Fact]
    public async Task Handler_RejectsUnspecifiedAccountId()
    {
        var handler = CreateHandler(
            new SequenceResolver([ConsoleSessionIdentityObservation.NoSession(4)]),
            new RecordingLogoffController());

        RemoteOperationHandlerResult result =
            await handler.HandleAsync(CreateRequest(ManagedWindowsAccountId.Unspecified), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.ProtocolViolation, result.ErrorCode);
    }

    [Fact]
    public async Task Service_WhenNoSession_ReturnsSuccessWithoutCallingWtsLogoff()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([ConsoleSessionIdentityObservation.NoSession(4)]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenExpectedPrimaryIsActive_CallsWtsLogoffOnce()
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([
                    ConsoleSessionIdentityObservation.User(7, PrimarySid),
                    ConsoleSessionIdentityObservation.User(7, PrimarySid)
                ]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, logoff.Calls);
        Assert.Equal(7u, logoff.SessionIds.Single());
    }

    [Fact]
    public async Task Service_WhenExpectedSecondaryIsActive_CallsWtsLogoffOnce()
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([
                    ConsoleSessionIdentityObservation.User(8, SecondarySid),
                    ConsoleSessionIdentityObservation.User(8, SecondarySid)
                ]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Secondary, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, logoff.Calls);
        Assert.Equal(8u, logoff.SessionIds.Single());
    }

    [Theory]
    [InlineData(ClassroomManagedWindowsAccountTypes.Primary, SecondarySid)]
    [InlineData(ClassroomManagedWindowsAccountTypes.Secondary, PrimarySid)]
    [InlineData(ClassroomManagedWindowsAccountTypes.Primary, OtherSid)]
    public async Task Service_WhenAnotherSessionIsActive_ReturnsChangedWithoutLogoff(
        string requestedAccountId,
        string activeSid)
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([ConsoleSessionIdentityObservation.User(4, activeSid)]),
                logoff)
            .LogoffAsync(requestedAccountId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionChanged, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenConsoleStateIsUnknown_DoesNotLogoff()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([ConsoleSessionIdentityObservation.Unknown(null, "unknown")]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionUnknown, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenBindingIsMissing_ReturnsAccountNotConfigured()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([ConsoleSessionIdentityObservation.NoSession(4)]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Secondary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.AccountNotConfigured, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenBindingsAreCorrupt_ReturnsManagedAccountBindingsInvalid()
    {
        await EnsureInstallationIdentityAsync();
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, ManagedWindowsAccountBindingConstants.FileName),
            "{ corrupt");
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([ConsoleSessionIdentityObservation.NoSession(4)]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.ManagedAccountBindingsInvalid, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_AllowsRenamedOrDeletedAccountWhenTokenSidMatchesBinding()
    {
        await SaveBindingsAsync([
            PrimaryBinding(accountReference: "DELETED-OR-RENAMED\\Primaria", windowsSid: PrimarySid)
        ]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([
                    ConsoleSessionIdentityObservation.User(4, PrimarySid),
                    ConsoleSessionIdentityObservation.User(4, PrimarySid)
                ]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenAccountReferenceMatchesButSidDiffers_DoesNotLogoff()
    {
        await SaveBindingsAsync([PrimaryBinding(accountReference: "PC23\\Student", windowsSid: PrimarySid)]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([ConsoleSessionIdentityObservation.User(4, OtherSid)]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionChanged, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenSessionIdChangesBeforeDestructiveCall_DoesNotLogoff()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([
                    ConsoleSessionIdentityObservation.User(4, PrimarySid),
                    ConsoleSessionIdentityObservation.User(5, PrimarySid)
                ]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionChanged, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenSidChangesBeforeDestructiveCall_DoesNotLogoff()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([
                    ConsoleSessionIdentityObservation.User(4, PrimarySid),
                    ConsoleSessionIdentityObservation.User(4, OtherSid)
                ]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionChanged, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenConsoleBecomesUnavailableBeforeDestructiveCall_DoesNotLogoff()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var logoff = new RecordingLogoffController();

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([
                    ConsoleSessionIdentityObservation.User(4, PrimarySid),
                    ConsoleSessionIdentityObservation.Unknown(
                        WindowsConsoleSessionResolver.WtsNoSession,
                        "console unavailable")
                ]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionUnknown, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WtsLogoffFailureReturnsStructuredError()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var logoff = new RecordingLogoffController { Succeeds = false, Win32Error = 5 };

        WindowsSessionLogoffServiceResult result = await CreateService(
                new SequenceResolver([
                    ConsoleSessionIdentityObservation.User(4, PrimarySid),
                    ConsoleSessionIdentityObservation.User(4, PrimarySid)
                ]),
                logoff)
            .LogoffAsync(ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsLogoffFailed, result.ErrorCode);
        Assert.Equal(1, logoff.Calls);
    }

    [Fact]
    public void Controller_UsesCurrentServerHandleAndDoesNotWait()
    {
        var native = new RecordingLogoffNativeApi { Succeeds = true };
        var controller = new WindowsSessionLogoffController(native);

        bool result = controller.Logoff(4, out int win32Error);

        Assert.True(result);
        Assert.Equal(0, win32Error);
        Assert.Equal(IntPtr.Zero, native.ServerHandle);
        Assert.Equal(4u, native.SessionId);
        Assert.False(native.Wait);
    }

    [Fact]
    public void ServiceCollection_RegistersLogoffHandler()
    {
        var services = new ServiceCollection();

        services.AddMasterNetworkTransportServices(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRemoteOperationHandler)
            && descriptor.ImplementationType == typeof(LogoffWindowsSessionOperationHandler));
    }

    [Fact]
    public void Implementation_DoesNotUseExternalShellOrProcess()
    {
        string source = File.ReadAllText(FindAgentSourceFile(
            "GaltekClassroom.Agent.Service",
            "WindowsSessions",
            "WindowsSessionLogoff.cs"));

        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("shutdown.exe", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logoff.exe", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PowerShell", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cmd.exe", source, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private LogoffWindowsSessionOperationHandler CreateHandler(
        IWindowsConsoleSessionResolver resolver,
        RecordingLogoffController logoff)
    {
        return new LogoffWindowsSessionOperationHandler(
            CreateService(resolver, logoff),
            NullLogger<LogoffWindowsSessionOperationHandler>.Instance);
    }

    private WindowsSessionLogoffService CreateService(
        IWindowsConsoleSessionResolver resolver,
        RecordingLogoffController logoff)
    {
        return new WindowsSessionLogoffService(
            resolver,
            logoff,
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            CreateStore(),
            NullLogger<WindowsSessionLogoffService>.Instance);
    }

    private ManagedWindowsAccountBindingStore CreateStore()
    {
        return new ManagedWindowsAccountBindingStore(
            new ManagedWindowsAccountBindingStoreOptions(_dataDirectory),
            new NoOpManagedWindowsAccountBindingFileSecurity());
    }

    private async Task SaveBindingsAsync(IReadOnlyList<ManagedWindowsAccountBinding> bindings)
    {
        await EnsureInstallationIdentityAsync();
        var store = CreateStore();
        foreach (ManagedWindowsAccountBinding binding in bindings)
        {
            ManagedWindowsAccountBindingStoreWriteResult result =
                await store.AddAsync(InstallationId, binding, CancellationToken.None);
            Assert.True(result.Succeeded);
        }
    }

    private async Task EnsureInstallationIdentityAsync()
    {
        var store = new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory));
        InstallationIdentityStoreReadResult existing = await store.ReadAsync(CancellationToken.None);
        if (existing.Status == InstallationIdentityStoreReadStatus.Loaded)
        {
            return;
        }

        await store.WriteNewAsync(InstallationIdentity.Create(
            InstallationId,
            ValidFingerprint(),
            FixedNow),
            CancellationToken.None);
    }

    private static OperationRequest CreateRequest(ManagedWindowsAccountId accountId)
    {
        return new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.LogoffWindowsSession,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds(),
            LogoffWindowsSession = new LogoffWindowsSessionOperationParameters
            {
                AccountId = accountId
            }
        };
    }

    private static ConsoleSessionIdentityObservation ObservationFor(string accountId, uint sessionId)
    {
        return ConsoleSessionIdentityObservation.User(
            sessionId,
            accountId == ClassroomManagedWindowsAccountTypes.Primary ? PrimarySid : SecondarySid);
    }

    private static ManagedWindowsAccountBinding PrimaryBinding(
        string accountReference = "PC23\\Primaria",
        string windowsSid = PrimarySid)
    {
        return ManagedWindowsAccountBinding.Create(
            ClassroomManagedWindowsAccountTypes.Primary,
            windowsSid,
            accountReference,
            FixedNow);
    }

    private static ManagedWindowsAccountBinding SecondaryBinding()
    {
        return ManagedWindowsAccountBinding.Create(
            ClassroomManagedWindowsAccountTypes.Secondary,
            SecondarySid,
            "PC23\\Secundaria",
            FixedNow);
    }

    private static HardwareFingerprint ValidFingerprint()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        return new HardwareFingerprint(hash, hash, hash, hash);
    }

    private static string FindAgentSourceFile(params string[] relativeParts)
    {
        DirectoryInfo? current = new(Environment.CurrentDirectory);
        while (current is not null)
        {
            string[] parts = new string[relativeParts.Length + 2];
            parts[0] = current.FullName;
            parts[1] = "src";
            Array.Copy(relativeParts, 0, parts, 2, relativeParts.Length);
            string candidate = Path.Combine(parts);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Agent source file was not found.", string.Join("/", relativeParts));
    }

    private sealed class SequenceResolver : IWindowsConsoleSessionResolver
    {
        private readonly Queue<ConsoleSessionIdentityObservation> _observations;

        public SequenceResolver(IEnumerable<ConsoleSessionIdentityObservation> observations)
        {
            _observations = new Queue<ConsoleSessionIdentityObservation>(observations);
        }

        public Task<ConsoleSessionIdentityObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            if (_observations.Count == 0)
            {
                throw new InvalidOperationException("No more observations were configured.");
            }

            return Task.FromResult(_observations.Dequeue());
        }
    }

    private sealed class RecordingLogoffController : IWindowsSessionLogoffController
    {
        public bool Succeeds { get; init; } = true;
        public int Win32Error { get; init; }
        public int Calls { get; private set; }
        public List<uint> SessionIds { get; } = [];

        public bool Logoff(uint sessionId, out int win32Error)
        {
            Calls++;
            SessionIds.Add(sessionId);
            win32Error = Succeeds ? 0 : Win32Error;
            return Succeeds;
        }
    }

    private sealed class RecordingLogoffNativeApi : IWindowsSessionLogoffNativeApi
    {
        public bool Succeeds { get; init; }
        public IntPtr ServerHandle { get; private set; }
        public uint SessionId { get; private set; }
        public bool Wait { get; private set; }

        public bool WtsLogoffSession(IntPtr serverHandle, uint sessionId, bool wait)
        {
            ServerHandle = serverHandle;
            SessionId = sessionId;
            Wait = wait;
            return Succeeds;
        }

        public int GetLastWin32Error()
        {
            return 5;
        }
    }
}
