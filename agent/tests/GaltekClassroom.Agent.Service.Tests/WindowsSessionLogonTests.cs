using System.Text;
using System.Reflection;
using GaltekClassroom.Agent.Service.Applications;
using GaltekClassroom.Agent.Service.CredentialProviderBridge;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Ipc;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.Pairing;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Service.WindowsSessions;
using GaltekClassroom.Agent.Service.WindowsAccounts;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class WindowsSessionLogonTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 5, 13, 0, 0, TimeSpan.Zero);
    private const string PrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-1004";
    private const string SecondarySid = "S-1-5-21-1000000000-1000000000-1000000000-1005";
    private const string AdminSid = "S-1-5-21-1000000000-1000000000-1000000000-1007";
    private const string OtherSid = "S-1-5-21-1000000000-1000000000-1000000000-1006";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.WindowsSessionLogon.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly MutableClock _clock = new(FixedNow);
    private readonly CredentialProviderActivationStore _activationStore = new();
    private readonly FakeWindowsAccountResolver _resolver = new();
    private readonly FakeCredentialProtector _protector = new();

    [Theory]
    [InlineData(ManagedWindowsAccountId.Primary)]
    [InlineData(ManagedWindowsAccountId.Secondary)]
    [InlineData(ManagedWindowsAccountId.Admin)]
    public async Task Handler_AcceptsEveryManagedProfile(ManagedWindowsAccountId networkAccountId)
    {
        string accountId = networkAccountId switch
        {
            ManagedWindowsAccountId.Primary => ClassroomManagedWindowsAccountTypes.Primary,
            ManagedWindowsAccountId.Secondary => ClassroomManagedWindowsAccountTypes.Secondary,
            _ => ClassroomManagedWindowsAccountTypes.Admin
        };
        await ConfigureReadyAccountAsync(accountId);
        Task<long> listener = StartListener();
        var handler = CreateHandler(new SequenceResolver([
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4)
        ]));

        Task<RemoteOperationHandlerResult> operation =
            handler.HandleAsync(CreateRequest(networkAccountId), CancellationToken.None);
        await listener.WaitAsync(TimeSpan.FromSeconds(2));
        CompleteCurrentActivation(CredentialProviderLogonCompletionOutcome.Success);

        RemoteOperationHandlerResult result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
    }

    [Fact]
    public async Task Handler_RejectsUnspecifiedAccountId()
    {
        var handler = CreateHandler(new SequenceResolver([ConsoleSessionIdentityObservation.NoSession(4)]));

        RemoteOperationHandlerResult result =
            await handler.HandleAsync(CreateRequest(ManagedWindowsAccountId.Unspecified), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.ProtocolViolation, result.ErrorCode);
    }

    [Fact]
    public async Task Service_WhenTargetAlreadyActive_ReturnsSuccessWithoutActivationOrDpapi()
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Primary);
        WindowsSessionLogonService service = CreateService(
            new SequenceResolver([ConsoleSessionIdentityObservation.User(4, PrimarySid)]));

        WindowsSessionLogonServiceResult result =
            await service.LogonAsync("operation-active", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(_activationStore.GetPending(FixedNow));
        Assert.Equal(0, _protector.UnprotectCalls);
    }

    [Theory]
    [InlineData(ClassroomManagedWindowsAccountTypes.Primary, SecondarySid)]
    [InlineData(ClassroomManagedWindowsAccountTypes.Secondary, PrimarySid)]
    [InlineData(ClassroomManagedWindowsAccountTypes.Primary, OtherSid)]
    public async Task Service_WhenAnotherSessionIsActive_ReturnsChangedWithoutActivation(
        string requestedAccountId,
        string activeSid)
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Primary);
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Secondary);
        WindowsSessionLogonService service = CreateService(
            new SequenceResolver([ConsoleSessionIdentityObservation.User(4, activeSid)]));

        WindowsSessionLogonServiceResult result =
            await service.LogonAsync("operation-other", requestedAccountId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionChanged, result.ErrorCode);
        Assert.Null(_activationStore.GetPending(FixedNow));
    }

    [Fact]
    public async Task Service_WhenSessionUnknown_ReturnsUnknownWithoutActivation()
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Primary);
        WindowsSessionLogonService service = CreateService(
            new SequenceResolver([ConsoleSessionIdentityObservation.Unknown(null, "unknown")]));

        WindowsSessionLogonServiceResult result =
            await service.LogonAsync("operation-unknown", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionUnknown, result.ErrorCode);
        Assert.Null(_activationStore.GetPending(FixedNow));
    }

    [Fact]
    public async Task Service_WhenAccountIsMissing_ReturnsAccountNotConfigured()
    {
        await EnsureInstallationIdentityAsync();
        WindowsSessionLogonService service = CreateService(
            new SequenceResolver([ConsoleSessionIdentityObservation.NoSession(4)]));

        WindowsSessionLogonServiceResult result =
            await service.LogonAsync("operation-missing-account", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.AccountNotConfigured, result.ErrorCode);
    }

    [Fact]
    public async Task Service_WhenCredentialIsMissing_ReturnsManagedCredentialNotConfigured()
    {
        await SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Primary, PrimarySid);
        WindowsSessionLogonService service = CreateService(
            new SequenceResolver([ConsoleSessionIdentityObservation.NoSession(4)]));

        WindowsSessionLogonServiceResult result =
            await service.LogonAsync("operation-missing-credential", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.ManagedCredentialNotConfigured, result.ErrorCode);
    }

    [Fact]
    public async Task Service_WhenAdminAlreadyActive_IsIdempotent()
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Admin);
        WindowsSessionLogonService service = CreateService(
            new SequenceResolver([ConsoleSessionIdentityObservation.User(4, AdminSid)]));

        WindowsSessionLogonServiceResult result = await service.LogonAsync(
            "operation-admin-active", ClassroomManagedWindowsAccountTypes.Admin, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(_activationStore.GetPending(FixedNow));
    }

    [Theory]
    [InlineData(false, true, false, NetworkOperationErrorCode.WindowsAccountAdminRequired)]
    [InlineData(true, false, false, NetworkOperationErrorCode.WindowsAccountDisabled)]
    [InlineData(true, true, true, NetworkOperationErrorCode.WindowsAccountBuiltIn)]
    public async Task Service_AdminStructuralPreflightRejectsUnsafeLiveAccount(
        bool administrator,
        bool enabled,
        bool builtIn,
        NetworkOperationErrorCode expectedError)
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Admin);
        WindowsSessionLogonService service = CreateService(
            new SequenceResolver([ConsoleSessionIdentityObservation.NoSession(4)]),
            inventory: new FixedInventory([
                new("Admin", "Admin", enabled, administrator, builtIn, AdminSid)
            ]));

        WindowsSessionLogonServiceResult result = await service.LogonAsync(
            "operation-admin-invalid", ClassroomManagedWindowsAccountTypes.Admin, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.Null(_activationStore.GetPending(FixedNow));
    }

    [Fact]
    public async Task Service_WhenConsoleChangesBeforeActivation_DoesNotCreateActivation()
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Primary);
        Task<long> listener = StartListener();
        WindowsSessionLogonService service = CreateService(new SequenceResolver([
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.User(4, OtherSid)
        ]));

        WindowsSessionLogonServiceResult result =
            await service.LogonAsync("operation-race", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionChanged, result.ErrorCode);
        Assert.Null(_activationStore.GetPending(FixedNow));
        Assert.False(listener.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Service_WhenProviderListenerUnavailable_ReturnsCredentialProviderUnavailable()
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Primary);
        WindowsSessionLogonService service = CreateService(
            new SequenceResolver([
                ConsoleSessionIdentityObservation.NoSession(4),
                ConsoleSessionIdentityObservation.NoSession(4)
            ]),
            new WindowsSessionLogonOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1)));

        WindowsSessionLogonServiceResult result =
            await service.LogonAsync("operation-no-listener", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.CredentialProviderUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task Service_CreatesRemoteActivationWithOperationIdAndAutoSubmit()
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Primary);
        Task<long> listener = StartListener();
        WindowsSessionLogonService service = CreateService(new SequenceResolver([
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4)
        ]));

        Task<WindowsSessionLogonServiceResult> operation =
            service.LogonAsync("operation-activation", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);
        await listener.WaitAsync(TimeSpan.FromSeconds(2));
        CredentialProviderActivation activation = _activationStore.GetPending(FixedNow)!;

        Assert.Equal("operation-activation", activation.OperationId);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Primary, activation.AccountId);
        Assert.True(activation.AutoSubmitRequested);
        Assert.Equal(CredentialProviderActivationSource.Remote, activation.Source);

        CompleteCurrentActivation(CredentialProviderLogonCompletionOutcome.Success);
        Assert.True((await operation.WaitAsync(TimeSpan.FromSeconds(2))).Succeeded);
    }

    [Fact]
    public async Task Service_WhenAnotherRemoteActivationExists_ReturnsBusy()
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Primary);
        _activationStore.SetRemotePending(
            "operation-existing",
            ClassroomManagedWindowsAccountTypes.Secondary,
            FixedNow,
            TimeSpan.FromSeconds(30),
            autoSubmitRequested: true);
        Task<long> listener = StartListener();
        WindowsSessionLogonService service = CreateService(new SequenceResolver([
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4)
        ]));

        WindowsSessionLogonServiceResult result =
            await service.LogonAsync("operation-new", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsLogonBusy, result.ErrorCode);
        Assert.Equal("operation-existing", _activationStore.GetPending(FixedNow)!.OperationId);
        Assert.False(listener.IsFaulted);
    }

    [Theory]
    [InlineData(CredentialProviderLogonCompletionOutcome.Failed, NetworkOperationErrorCode.WindowsLogonFailed)]
    [InlineData(CredentialProviderLogonCompletionOutcome.LocalSerializationFailed, NetworkOperationErrorCode.WindowsLogonFailed)]
    [InlineData(CredentialProviderLogonCompletionOutcome.TimedOut, NetworkOperationErrorCode.WindowsLogonNotConfirmed)]
    public async Task Service_MapsCompletionOutcomes(
        CredentialProviderLogonCompletionOutcome outcome,
        NetworkOperationErrorCode expectedError)
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Primary);
        Task<long> listener = StartListener();
        WindowsSessionLogonService service = CreateService(
            new SequenceResolver([
                ConsoleSessionIdentityObservation.NoSession(4),
                ConsoleSessionIdentityObservation.NoSession(4)
            ]),
            new WindowsSessionLogonOptions(
                outcome == CredentialProviderLogonCompletionOutcome.TimedOut
                    ? TimeSpan.FromMilliseconds(100)
                    : TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(1)));

        Task<WindowsSessionLogonServiceResult> operation =
            service.LogonAsync($"operation-{outcome}", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);
        await listener.WaitAsync(TimeSpan.FromSeconds(2));
        if (outcome != CredentialProviderLogonCompletionOutcome.TimedOut)
        {
            CompleteCurrentActivation(outcome);
        }

        WindowsSessionLogonServiceResult result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorCode);
    }

    [Fact]
    public async Task Dispatcher_DedupesLogonByOperationIdAndAccountId()
    {
        var handler = new CountingLogonHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            _clock);

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(
            CreateRequest(ManagedWindowsAccountId.Primary, "operation-dedupe"),
            CancellationToken.None);
        RemoteOperationDispatchResult same = await dispatcher.DispatchAsync(
            CreateRequest(ManagedWindowsAccountId.Primary, "operation-dedupe"),
            CancellationToken.None);
        RemoteOperationDispatchResult different = await dispatcher.DispatchAsync(
            CreateRequest(ManagedWindowsAccountId.Secondary, "operation-dedupe"),
            CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.True(same.Duplicate);
        Assert.True(different.Duplicate);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(OperationExecutionStatus.Success, same.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.OperationDuplicate, different.Result.ErrorCode);
    }

    [Fact]
    public void ServiceCollection_RegistersLogonHandler()
    {
        var services = new ServiceCollection();

        services.AddMasterNetworkTransportServices(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRemoteOperationHandler)
            && descriptor.ImplementationType == typeof(LogonManagedAccountOperationHandler));
    }

    [Fact]
    public async Task ServiceCollection_BridgeAndLogonShareCredentialProviderActivationAuthority()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddInstallationIdentityServices();
        services.AddCommercialLicenseServices();
        services.AddMasterAuthorizationServices();
        services.AddNetworkIdentityServices();
        services.AddClientPairingServices();
        services.AddMasterNetworkTransportServices(new ConfigurationBuilder().Build());
        services.AddLocalIpcServices();
        services.AddSessionCommandServices();
        services.AddApplicationBindingServices();
        services.AddManagedWindowsAccountBindingServices();
        services.AddCredentialProviderBridgeServices();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        var store = provider.GetRequiredService<ICredentialProviderActivationStore>();
        var activationService = provider.GetRequiredService<CredentialProviderActivationService>();
        var bridgeHandler = provider.GetRequiredService<CredentialProviderBridgeRequestHandler>();
        var logonService = provider.GetRequiredService<WindowsSessionLogonService>();

        var logonActivationService = typeof(WindowsSessionLogonService)
            .GetField("_activationService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(logonService);
        var activationStore = typeof(CredentialProviderActivationService)
            .GetField("_activationStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(activationService);

        Assert.Same(activationService, logonActivationService);
        Assert.Same(store, activationStore);

        bridgeHandler.MarkListenerObserved();

        Assert.Equal(
            CredentialProviderListenerAvailability.FreshPresence,
            await activationService.WaitForListenerAvailabilityAsync(TimeSpan.Zero, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private LogonManagedAccountOperationHandler CreateHandler(IWindowsConsoleSessionResolver sessionResolver)
    {
        return new LogonManagedAccountOperationHandler(
            CreateService(sessionResolver),
            NullLogger<LogonManagedAccountOperationHandler>.Instance);
    }

    private WindowsSessionLogonService CreateService(
        IWindowsConsoleSessionResolver sessionResolver,
        WindowsSessionLogonOptions? options = null,
        IWindowsAccountInventorySource? inventory = null)
    {
        return new WindowsSessionLogonService(
            new WindowsSessionStateService(
                sessionResolver,
                new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
                BindingStore()),
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            BindingStore(),
            _resolver,
            CredentialStore(),
            inventory ?? new FixedInventory([
                new("Primaria", "Primaria", true, false, false, PrimarySid),
                new("Secundaria", "Secundaria", true, false, false, SecondarySid),
                new("Admin", "Admin", true, true, false, AdminSid),
                new("Otra", "Otra", true, false, false, OtherSid)
            ]),
            ActivationService(),
            options ?? new WindowsSessionLogonOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)),
            NullLogger<WindowsSessionLogonService>.Instance);
    }

    private CredentialProviderActivationService ActivationService()
    {
        return new CredentialProviderActivationService(
            _activationStore,
            _clock,
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            BindingStore(),
            _resolver,
            CredentialStore());
    }

    private ManagedWindowsAccountBindingStore BindingStore()
    {
        return new ManagedWindowsAccountBindingStore(
            new ManagedWindowsAccountBindingStoreOptions(_dataDirectory),
            new NoOpManagedWindowsAccountBindingFileSecurity());
    }

    private ManagedWindowsCredentialStore CredentialStore()
    {
        return new ManagedWindowsCredentialStore(
            new ManagedWindowsCredentialStoreOptions(_dataDirectory),
            BindingStore(),
            _resolver,
            _protector,
            new NoOpManagedWindowsCredentialFileSecurity(),
            _clock);
    }

    private async Task ConfigureReadyAccountAsync(string accountId)
    {
        string sid = accountId switch
        {
            ClassroomManagedWindowsAccountTypes.Primary => PrimarySid,
            ClassroomManagedWindowsAccountTypes.Secondary => SecondarySid,
            _ => AdminSid
        };
        await SaveBindingAsync(accountId, sid);
        ManagedWindowsCredentialWriteResult write = await CredentialStore().ReplaceUtf16LittleEndianAsync(
            InstallationId,
            accountId,
            Encoding.Unicode.GetBytes("secret"),
            CancellationToken.None);
        Assert.True(write.Succeeded, write.ErrorMessage);
    }

    private async Task SaveBindingAsync(string accountId, string sid)
    {
        await EnsureInstallationIdentityAsync();
        _resolver.Add(PrimarySid, "AULA", "Primaria", WindowsAccountSidNameUse.User);
        _resolver.Add(SecondarySid, "AULA", "Secundaria", WindowsAccountSidNameUse.User);
        _resolver.Add(AdminSid, "AULA", "Admin", WindowsAccountSidNameUse.User);
        _resolver.Add(OtherSid, "AULA", "Otra", WindowsAccountSidNameUse.User);
        ManagedWindowsAccountBindingStoreWriteResult write = await BindingStore().AddAsync(
            InstallationId,
            ManagedWindowsAccountBinding.Create(accountId, sid, $"AULA\\{accountId}", FixedNow),
            CancellationToken.None);
        Assert.True(write.Succeeded, write.ErrorMessage);
    }

    private async Task EnsureInstallationIdentityAsync()
    {
        Directory.CreateDirectory(_dataDirectory);
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

    private Task<long> StartListener()
    {
        return _activationStore.WaitForGenerationChangeAsync(
            _activationStore.CurrentGeneration(FixedNow),
            FixedNow,
            CancellationToken.None);
    }

    private void CompleteCurrentActivation(CredentialProviderLogonCompletionOutcome outcome)
    {
        CredentialProviderActivation activation = _activationStore.GetPending(FixedNow)!;
        Assert.True(_activationStore.TryComplete(activation.ActivationId, outcome, FixedNow));
    }

    private static OperationRequest CreateRequest(
        ManagedWindowsAccountId accountId,
        string? operationId = null)
    {
        return new OperationRequest
        {
            OperationId = operationId ?? Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.LogonManagedAccount,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds(),
            LogonManagedAccount = new LogonManagedAccountOperationParameters
            {
                AccountId = accountId
            }
        };
    }

    private static HardwareFingerprint ValidFingerprint()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        return new HardwareFingerprint(hash, hash, hash, hash);
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

    private sealed class MutableClock : ISystemClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeWindowsAccountResolver : IWindowsAccountResolver
    {
        private readonly Dictionary<string, WindowsAccountIdentity> _bySid = new(StringComparer.OrdinalIgnoreCase);

        public void Add(
            string windowsSid,
            string domain,
            string username,
            WindowsAccountSidNameUse use)
        {
            _bySid[windowsSid] = new WindowsAccountIdentity(
                windowsSid,
                $"{domain}\\{username}",
                use)
            {
                Domain = domain,
                Username = username
            };
        }

        public WindowsAccountResolution ResolveCurrentUser()
        {
            throw new NotSupportedException();
        }

        public WindowsAccountResolution ResolveAccount(string accountName)
        {
            throw new NotSupportedException();
        }

        public WindowsAccountResolution ResolveSid(string windowsSid)
        {
            return _bySid.TryGetValue(windowsSid, out var identity)
                ? WindowsAccountResolution.Resolved(identity)
                : WindowsAccountResolution.NotFound("not found");
        }
    }

    private sealed class FixedInventory(IReadOnlyList<WindowsLocalAccountRecord> accounts)
        : IWindowsAccountInventorySource
    {
        public IReadOnlyList<WindowsLocalAccountRecord> Read(CancellationToken cancellationToken) => accounts;
    }

    private sealed class FakeCredentialProtector : IManagedWindowsCredentialProtector
    {
        public int UnprotectCalls { get; private set; }

        public ManagedWindowsCredentialProtectionResult Protect(
            byte[] plaintext,
            byte[] optionalEntropy)
        {
            return ManagedWindowsCredentialProtectionResult.Protected(Transform(plaintext));
        }

        public ManagedWindowsCredentialProtectionResult Unprotect(
            byte[] protectedData,
            byte[] optionalEntropy)
        {
            UnprotectCalls++;
            return ManagedWindowsCredentialProtectionResult.Unprotected(Transform(protectedData));
        }

        private static byte[] Transform(byte[] data)
        {
            var copy = data.ToArray();
            for (var index = 0; index < copy.Length; index++)
            {
                copy[index] ^= 0xA5;
            }

            return copy;
        }
    }

    private sealed class CountingLogonHandler : IRemoteOperationHandler
    {
        public int Calls { get; private set; }

        public NetworkOperationType OperationType => NetworkOperationType.LogonManagedAccount;

        public Task<RemoteOperationHandlerResult> HandleAsync(
            OperationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(RemoteOperationHandlerResult.Success("logon complete"));
        }
    }
}
