using System.Text;
using GaltekClassroom.Agent.Service.CredentialProviderBridge;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.WindowsSessions;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class WindowsSessionSwitchTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 5, 15, 0, 0, TimeSpan.Zero);
    private const string PrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-1004";
    private const string SecondarySid = "S-1-5-21-1000000000-1000000000-1000000000-1005";
    private const string OtherSid = "S-1-5-21-1000000000-1000000000-1000000000-1006";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.WindowsSessionSwitch.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly MutableClock _clock = new(FixedNow);
    private readonly CredentialProviderActivationStore _activationStore = new();
    private readonly FakeWindowsAccountResolver _resolver = new();
    private readonly FakeCredentialProtector _protector = new();

    [Theory]
    [InlineData(ManagedWindowsAccountId.Primary, ClassroomManagedWindowsAccountTypes.Primary)]
    [InlineData(ManagedWindowsAccountId.Secondary, ClassroomManagedWindowsAccountTypes.Secondary)]
    public async Task Handler_AcceptsPrimaryAndSecondaryOnly(
        ManagedWindowsAccountId networkAccountId,
        string targetAccountId)
    {
        await ConfigureReadyAccountsAsync();
        var logoff = new RecordingLogoffController();
        var handler = CreateHandler(
            new SequenceResolver([ObservationFor(targetAccountId, 4)]),
            logoff);

        RemoteOperationHandlerResult result =
            await handler.HandleAsync(CreateRequest(networkAccountId), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(0, logoff.Calls);
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

    [Theory]
    [InlineData(ClassroomManagedWindowsAccountTypes.Primary)]
    [InlineData(ClassroomManagedWindowsAccountTypes.Secondary)]
    public async Task Service_WhenTargetAlreadyActive_ReturnsSuccessWithoutLogoffLogonOrDpapi(string targetAccountId)
    {
        await ConfigureReadyAccountsAsync();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(
            new SequenceResolver([ObservationFor(targetAccountId, 4)]),
            logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-active", targetAccountId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, logoff.Calls);
        Assert.Equal(0, _protector.UnprotectCalls);
        Assert.Null(_activationStore.GetPending(FixedNow));
    }

    [Theory]
    [InlineData(ClassroomManagedWindowsAccountTypes.Primary)]
    [InlineData(ClassroomManagedWindowsAccountTypes.Secondary)]
    public async Task Service_WhenNoSession_UsesLogonFlow(string targetAccountId)
    {
        await ConfigureReadyAccountsAsync();
        Task<long> listener = StartListener();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4)
        ]), logoff);

        Task<WindowsSessionSwitchServiceResult> operation =
            service.SwitchAsync("switch-logon", targetAccountId, CancellationToken.None);
        await listener.WaitAsync(TimeSpan.FromSeconds(2));
        CompleteCurrentActivation(CredentialProviderLogonCompletionOutcome.Success);

        WindowsSessionSwitchServiceResult result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Succeeded);
        Assert.Equal(0, logoff.Calls);
    }

    [Theory]
    [InlineData(ClassroomManagedWindowsAccountTypes.Primary, ClassroomManagedWindowsAccountTypes.Secondary)]
    [InlineData(ClassroomManagedWindowsAccountTypes.Secondary, ClassroomManagedWindowsAccountTypes.Primary)]
    public async Task Service_WhenOppositeManagedActive_PreflightsLogsOffWaitsAndLogsOn(
        string targetAccountId,
        string sourceAccountId)
    {
        await ConfigureReadyAccountsAsync();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ObservationFor(sourceAccountId, 4),
            ObservationFor(sourceAccountId, 4),
            ObservationFor(sourceAccountId, 4),
            ObservationFor(sourceAccountId, 4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4)
        ]), logoff);

        Task<WindowsSessionSwitchServiceResult> operation =
            service.SwitchAsync("switch-full", targetAccountId, CancellationToken.None);
        await logoff.WaitForCallAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, _activationStore.ListenerCount);
        Task<long> listener = StartListener();
        await listener.WaitAsync(TimeSpan.FromSeconds(2));
        CompleteCurrentActivation(CredentialProviderLogonCompletionOutcome.Success);

        WindowsSessionSwitchServiceResult result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Succeeded);
        Assert.Equal(1, logoff.Calls);
        Assert.Equal(4u, logoff.SessionIds.Single());
        Assert.True(_protector.UnprotectCalls >= 1);
    }

    [Theory]
    [InlineData(OtherSid, NetworkOperationErrorCode.WindowsSessionChanged)]
    [InlineData(null, NetworkOperationErrorCode.WindowsSessionUnknown)]
    public async Task Service_WhenOtherOrUnknownSessionActive_DoesNotLogoffOrLogon(
        string? activeSid,
        NetworkOperationErrorCode expectedError)
    {
        await ConfigureReadyAccountsAsync();
        var logoff = new RecordingLogoffController();
        ConsoleSessionIdentityObservation observation = activeSid is null
            ? ConsoleSessionIdentityObservation.Unknown(null, "unknown")
            : ConsoleSessionIdentityObservation.User(4, activeSid);
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([observation]), logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-blocked", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
        Assert.Null(_activationStore.GetPending(FixedNow));
    }

    [Fact]
    public async Task Service_WhenTargetBindingMissing_DoesNotCloseSource()
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Secondary);
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(
            new SequenceResolver([ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4)]),
            logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-missing-binding", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.AccountNotConfigured, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenTargetCredentialMissing_DoesNotCloseSource()
    {
        await SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Primary, PrimarySid);
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Secondary);
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(
            new SequenceResolver([ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4)]),
            logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-missing-credential", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.ManagedCredentialNotConfigured, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenTargetAccountMissing_DoesNotCloseSource()
    {
        await SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Primary, PrimarySid);
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Secondary);
        _resolver.Remove(PrimarySid);
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(
            new SequenceResolver([ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4)]),
            logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-missing-account", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.AccountNotFound, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenTargetCredentialStoreIsCorrupt_DoesNotCloseSource()
    {
        await ConfigureReadyAccountsAsync();
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, ManagedWindowsCredentialConstants.FileName),
            "{ corrupt");
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(
            new SequenceResolver([ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4)]),
            logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-corrupt-store", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.ManagedCredentialStoreInvalid, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenProviderUnavailableBeforeLogoff_DoesNotBlockLogoffAndLogonContinuesAfterNoSession()
    {
        await ConfigureReadyAccountsAsync();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4)
        ]), logoff, logonOptions: new WindowsSessionLogonOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));

        Task<WindowsSessionSwitchServiceResult> operation =
            service.SwitchAsync("switch-provider-after-logoff", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);
        await logoff.WaitForCallAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, logoff.Calls);
        Assert.Equal(0, _activationStore.ListenerCount);
        Task<long> listener = StartListener();
        await listener.WaitAsync(TimeSpan.FromSeconds(2));
        CompleteCurrentActivation(CredentialProviderLogonCompletionOutcome.Success);

        WindowsSessionSwitchServiceResult result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Succeeded);
        Assert.Equal(1, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenProviderUnavailableAfterNoSession_ReturnsFailureAfterClosingSource()
    {
        await ConfigureReadyAccountsAsync();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4)
        ]), logoff, logonOptions: new WindowsSessionLogonOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1)));

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-provider-unavailable", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.CredentialProviderUnavailable, result.ErrorCode);
        Assert.Equal(1, logoff.Calls);
        Assert.Null(_activationStore.GetPending(FixedNow));
    }

    [Fact]
    public async Task Service_WhenSourceChangesBeforeDestructiveLogoff_DoesNotLogoff()
    {
        await ConfigureReadyAccountsAsync();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ConsoleSessionIdentityObservation.User(4, OtherSid)
        ]), logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-source-race", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionChanged, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenSourceStillActiveAfterAcceptedLogoff_WaitsBeforeLogon()
    {
        await ConfigureReadyAccountsAsync();
        Task<long> listener = StartListener();
        var logoff = new RecordingLogoffController();
        var delay = new RecordingSwitchDelay();
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4)
        ]), logoff, delay);

        Task<WindowsSessionSwitchServiceResult> operation =
            service.SwitchAsync("switch-wait-source", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);
        await listener.WaitAsync(TimeSpan.FromSeconds(2));
        CompleteCurrentActivation(CredentialProviderLogonCompletionOutcome.Success);

        WindowsSessionSwitchServiceResult result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Succeeded);
        Assert.True(delay.Calls >= 1);
        Assert.Equal(1, logoff.Calls);
    }

    [Theory]
    [InlineData(OtherSid, NetworkOperationErrorCode.WindowsSessionChanged)]
    [InlineData(null, NetworkOperationErrorCode.WindowsSessionUnknown)]
    public async Task Service_WhenTransitionChangesToOtherOrUnknown_AbortsWithoutLogon(
        string? activeSid,
        NetworkOperationErrorCode expectedError)
    {
        await ConfigureReadyAccountsAsync();
        Task<long> listener = StartListener();
        var logoff = new RecordingLogoffController();
        ConsoleSessionIdentityObservation transition = activeSid is null
            ? ConsoleSessionIdentityObservation.Unknown(null, "unknown")
            : ConsoleSessionIdentityObservation.User(4, activeSid);
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            transition
        ]), logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-transition-changed", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.Equal(1, logoff.Calls);
        Assert.False(listener.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Service_WhenTargetAppearsDuringTransition_SucceedsWithoutActivation()
    {
        await ConfigureReadyAccountsAsync();
        Task<long> listener = StartListener();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Primary, 5)
        ]), logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-target-appeared", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, logoff.Calls);
        Assert.Null(_activationStore.GetPending(FixedNow));
        Assert.False(listener.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Service_WhenNoSessionChangesBeforeActivation_LogonServiceBlocks()
    {
        await ConfigureReadyAccountsAsync();
        Task<long> listener = StartListener();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.User(4, OtherSid)
        ]), logoff);

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-logon-race", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionChanged, result.ErrorCode);
        Assert.Equal(0, logoff.Calls);
        Assert.Null(_activationStore.GetPending(FixedNow));
        Assert.False(listener.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(CredentialProviderLogonCompletionOutcome.Failed, NetworkOperationErrorCode.WindowsLogonFailed)]
    [InlineData(CredentialProviderLogonCompletionOutcome.LocalSerializationFailed, NetworkOperationErrorCode.WindowsLogonFailed)]
    [InlineData(CredentialProviderLogonCompletionOutcome.TimedOut, NetworkOperationErrorCode.WindowsLogonNotConfirmed)]
    public async Task Service_WhenLogonFailsAfterLogoff_ReturnsFailureWithoutRollback(
        CredentialProviderLogonCompletionOutcome outcome,
        NetworkOperationErrorCode expectedError)
    {
        await ConfigureReadyAccountsAsync();
        Task<long> listener = StartListener();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(new SequenceResolver([
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4),
            ConsoleSessionIdentityObservation.NoSession(4)
        ]), logoff, logonOptions: new WindowsSessionLogonOptions(
            outcome == CredentialProviderLogonCompletionOutcome.TimedOut
                ? TimeSpan.FromMilliseconds(100)
                : TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(1)));

        Task<WindowsSessionSwitchServiceResult> operation =
            service.SwitchAsync($"switch-logon-{outcome}", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);
        await listener.WaitAsync(TimeSpan.FromSeconds(2));
        if (outcome != CredentialProviderLogonCompletionOutcome.TimedOut)
        {
            CompleteCurrentActivation(outcome);
        }

        WindowsSessionSwitchServiceResult result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.Equal(1, logoff.Calls);
    }

    [Fact]
    public async Task Service_WhenNoSessionIsNeverConfirmed_ReturnsSwitchNotConfirmedWithoutLogon()
    {
        await ConfigureReadyAccountsAsync();
        Task<long> listener = StartListener();
        var logoff = new RecordingLogoffController();
        WindowsSessionSwitchService service = CreateService(new StickyResolver(
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4)),
            logoff,
            options: new WindowsSessionSwitchOptions(TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1)));

        WindowsSessionSwitchServiceResult result =
            await service.SwitchAsync("switch-not-confirmed", ClassroomManagedWindowsAccountTypes.Primary, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSwitchNotConfirmed, result.ErrorCode);
        Assert.Equal(1, logoff.Calls);
        Assert.Null(_activationStore.GetPending(FixedNow));
        Assert.False(listener.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Service_WhenCancelledDuringTransition_DoesNotStartLogon()
    {
        await ConfigureReadyAccountsAsync();
        _ = StartListener();
        var logoff = new RecordingLogoffController();
        using var cts = new CancellationTokenSource();
        var delay = new CancellingSwitchDelay(cts);
        WindowsSessionSwitchService service = CreateService(new StickyResolver(
            ObservationFor(ClassroomManagedWindowsAccountTypes.Secondary, 4)),
            logoff,
            delay,
            options: new WindowsSessionSwitchOptions(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SwitchAsync("switch-cancelled", ClassroomManagedWindowsAccountTypes.Primary, cts.Token));

        Assert.Equal(1, logoff.Calls);
        Assert.Null(_activationStore.GetPending(FixedNow));
    }

    [Fact]
    public async Task Dispatcher_DedupesSwitchByOperationIdAndTargetAccountId()
    {
        var handler = new CountingSwitchHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            _clock);

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(
            CreateRequest(ManagedWindowsAccountId.Primary, "switch-dedupe"),
            CancellationToken.None);
        RemoteOperationDispatchResult same = await dispatcher.DispatchAsync(
            CreateRequest(ManagedWindowsAccountId.Primary, "switch-dedupe"),
            CancellationToken.None);
        RemoteOperationDispatchResult different = await dispatcher.DispatchAsync(
            CreateRequest(ManagedWindowsAccountId.Secondary, "switch-dedupe"),
            CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.True(same.Duplicate);
        Assert.True(different.Duplicate);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(OperationExecutionStatus.Success, same.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.OperationDuplicate, different.Result.ErrorCode);
    }

    [Fact]
    public async Task Dispatcher_TransportLostAfterSwitchLeavesOriginalUnknownWithoutRetry()
    {
        var handler = new BlockingSwitchHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions { SwitchManagedAccountTimeout = TimeSpan.FromMilliseconds(5) },
            _clock);

        RemoteOperationDispatchResult result = await dispatcher.DispatchAsync(
            CreateRequest(ManagedWindowsAccountId.Primary, "switch-timeout"),
            CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.OperationTimeout, result.Result.ErrorCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void ServiceCollection_RegistersSwitchHandler()
    {
        var services = new ServiceCollection();

        services.AddMasterNetworkTransportServices(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRemoteOperationHandler)
            && descriptor.ImplementationType == typeof(SwitchManagedAccountOperationHandler));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private SwitchManagedAccountOperationHandler CreateHandler(
        IWindowsConsoleSessionResolver sessionResolver,
        RecordingLogoffController logoff)
    {
        return new SwitchManagedAccountOperationHandler(
            CreateService(sessionResolver, logoff),
            NullLogger<SwitchManagedAccountOperationHandler>.Instance);
    }

    private WindowsSessionSwitchService CreateService(
        IWindowsConsoleSessionResolver sessionResolver,
        RecordingLogoffController logoff,
        IWindowsSessionSwitchDelay? delay = null,
        WindowsSessionSwitchOptions? options = null,
        WindowsSessionLogonOptions? logonOptions = null)
    {
        var stateService = new WindowsSessionStateService(
            sessionResolver,
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            BindingStore());
        var logonService = new WindowsSessionLogonService(
            stateService,
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            BindingStore(),
            _resolver,
            CredentialStore(),
            ActivationService(),
            logonOptions ?? new WindowsSessionLogonOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)),
            NullLogger<WindowsSessionLogonService>.Instance);

        return new WindowsSessionSwitchService(
            stateService,
            new WindowsSessionLogoffService(
                sessionResolver,
                logoff,
                new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
                BindingStore(),
                NullLogger<WindowsSessionLogoffService>.Instance),
            logonService,
            options ?? new WindowsSessionSwitchOptions(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(1)),
            delay ?? new WindowsSessionSwitchDelay(),
            NullLogger<WindowsSessionSwitchService>.Instance);
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

    private async Task ConfigureReadyAccountsAsync()
    {
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Primary);
        await ConfigureReadyAccountAsync(ClassroomManagedWindowsAccountTypes.Secondary);
    }

    private async Task ConfigureReadyAccountAsync(string accountId)
    {
        string sid = SidFor(accountId);
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
            OperationType = NetworkOperationType.SwitchManagedAccount,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds(),
            SwitchManagedAccount = new SwitchManagedAccountOperationParameters
            {
                AccountId = accountId
            }
        };
    }

    private static ConsoleSessionIdentityObservation ObservationFor(string accountId, uint sessionId)
    {
        return ConsoleSessionIdentityObservation.User(sessionId, SidFor(accountId));
    }

    private static string SidFor(string accountId)
    {
        return accountId == ClassroomManagedWindowsAccountTypes.Primary ? PrimarySid : SecondarySid;
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

    private sealed class StickyResolver : IWindowsConsoleSessionResolver
    {
        private readonly ConsoleSessionIdentityObservation _observation;

        public StickyResolver(ConsoleSessionIdentityObservation observation)
        {
            _observation = observation;
        }

        public Task<ConsoleSessionIdentityObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_observation);
        }
    }

    private sealed class RecordingLogoffController : IWindowsSessionLogoffController
    {
        private readonly TaskCompletionSource _called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }
        public List<uint> SessionIds { get; } = [];

        public bool Logoff(uint sessionId, out int win32Error)
        {
            Calls++;
            SessionIds.Add(sessionId);
            _called.TrySetResult();
            win32Error = 0;
            return true;
        }

        public Task WaitForCallAsync(TimeSpan timeout)
        {
            return _called.Task.WaitAsync(timeout);
        }
    }

    private sealed class RecordingSwitchDelay : IWindowsSessionSwitchDelay
    {
        public int Calls { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.Delay(delay, cancellationToken);
        }
    }

    private sealed class CancellingSwitchDelay : IWindowsSessionSwitchDelay
    {
        private readonly CancellationTokenSource _source;

        public CancellingSwitchDelay(CancellationTokenSource source)
        {
            _source = source;
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            _source.Cancel();
            return Task.FromCanceled(cancellationToken);
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

        public void Add(string windowsSid, string domain, string username, WindowsAccountSidNameUse use)
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

        public void Remove(string windowsSid)
        {
            _bySid.Remove(windowsSid);
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

    private sealed class CountingSwitchHandler : IRemoteOperationHandler
    {
        public int Calls { get; private set; }

        public NetworkOperationType OperationType => NetworkOperationType.SwitchManagedAccount;

        public Task<RemoteOperationHandlerResult> HandleAsync(
            OperationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(RemoteOperationHandlerResult.Success("switch complete"));
        }
    }

    private sealed class BlockingSwitchHandler : IRemoteOperationHandler
    {
        public int Calls { get; private set; }

        public NetworkOperationType OperationType => NetworkOperationType.SwitchManagedAccount;

        public async Task<RemoteOperationHandlerResult> HandleAsync(
            OperationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return RemoteOperationHandlerResult.Success("unexpected");
        }
    }
}
