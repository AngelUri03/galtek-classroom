using System.Diagnostics;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using ProtoWindowsSessionState = GaltekClassroom.Protocol.Network.V1.WindowsSessionState;

namespace GaltekClassroom.Agent.Service.WindowsSessions;

public sealed record WindowsSessionSwitchOptions(
    TimeSpan PostLogoffWait,
    TimeSpan PostLogoffPollInterval)
{
    public static WindowsSessionSwitchOptions Default { get; } = new(
        TimeSpan.FromSeconds(24),
        TimeSpan.FromMilliseconds(300));
}

public interface IWindowsSessionSwitchClock
{
    long GetTimestamp();

    TimeSpan GetElapsedTime(long startingTimestamp);
}

public sealed class WindowsSessionSwitchClock : IWindowsSessionSwitchClock
{
    public long GetTimestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    public TimeSpan GetElapsedTime(long startingTimestamp)
    {
        return Stopwatch.GetElapsedTime(startingTimestamp);
    }
}

public interface IWindowsSessionSwitchDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class WindowsSessionSwitchDelay : IWindowsSessionSwitchDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}

public sealed record WindowsSessionSwitchServiceResult(
    bool Succeeded,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static WindowsSessionSwitchServiceResult Success(string message)
    {
        return new WindowsSessionSwitchServiceResult(true, NetworkOperationErrorCode.Unspecified, message);
    }

    public static WindowsSessionSwitchServiceResult Failure(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new WindowsSessionSwitchServiceResult(false, errorCode, message);
    }
}

public sealed class WindowsSessionSwitchService
{
    private readonly WindowsSessionStateService _sessionStateService;
    private readonly WindowsSessionLogoffService _logoffService;
    private readonly WindowsSessionLogonService _logonService;
    private readonly WindowsSessionSwitchOptions _options;
    private readonly IWindowsSessionSwitchClock _clock;
    private readonly IWindowsSessionSwitchDelay _delay;
    private readonly ILogger<WindowsSessionSwitchService> _logger;

    public WindowsSessionSwitchService(
        WindowsSessionStateService sessionStateService,
        WindowsSessionLogoffService logoffService,
        WindowsSessionLogonService logonService,
        WindowsSessionSwitchOptions options,
        IWindowsSessionSwitchClock clock,
        IWindowsSessionSwitchDelay delay,
        ILogger<WindowsSessionSwitchService> logger)
    {
        _sessionStateService = sessionStateService;
        _logoffService = logoffService;
        _logonService = logonService;
        _options = options;
        _clock = clock;
        _delay = delay;
        _logger = logger;
    }

    public async Task<WindowsSessionSwitchServiceResult> SwitchAsync(
        string operationId,
        string targetAccountId,
        CancellationToken cancellationToken)
    {
        WindowsSessionStateServiceResult initial =
            await _sessionStateService.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!initial.Succeeded)
        {
            return Failure(initial.ErrorCode, initial.Message);
        }

        if (StateMatchesAccount(initial.State, targetAccountId))
        {
            return Success("Expected managed Windows session is already active.");
        }

        if (initial.State == ProtoWindowsSessionState.NoSession)
        {
            return FromLogon(await _logonService.LogonAsync(
                operationId,
                targetAccountId,
                cancellationToken).ConfigureAwait(false));
        }

        string? sourceAccountId = AccountIdFromState(initial.State);
        if (sourceAccountId is null)
        {
            return initial.State == ProtoWindowsSessionState.Unknown
                ? Failure(
                    NetworkOperationErrorCode.WindowsSessionUnknown,
                    "Windows console session identity could not be determined.")
                : Failure(
                    NetworkOperationErrorCode.WindowsSessionChanged,
                    "Another Windows session is already active.");
        }

        WindowsSessionLogonServiceResult preflight =
            await _logonService.PreflightTargetAsync(targetAccountId, cancellationToken).ConfigureAwait(false);
        if (!preflight.Succeeded)
        {
            return Failure(preflight.ErrorCode, preflight.Message);
        }

        WindowsSessionStateServiceResult second =
            await _sessionStateService.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!second.Succeeded)
        {
            return Failure(second.ErrorCode, second.Message);
        }

        if (!StateMatchesAccount(second.State, sourceAccountId))
        {
            return second.State == ProtoWindowsSessionState.Unknown
                ? Failure(
                    NetworkOperationErrorCode.WindowsSessionUnknown,
                    "Windows console session identity could not be determined.")
                : Failure(
                    NetworkOperationErrorCode.WindowsSessionChanged,
                    "Windows console session changed before logoff.");
        }

        WindowsSessionLogoffServiceResult logoff =
            await _logoffService.LogoffAsync(sourceAccountId, cancellationToken).ConfigureAwait(false);
        if (!logoff.Succeeded)
        {
            return Failure(logoff.ErrorCode, logoff.Message);
        }

        _logger.LogInformation(
            "WINDOWS_SWITCH_LOGOFF_ACCEPTED source={SourceAccountId} target={TargetAccountId}",
            sourceAccountId,
            targetAccountId);

        WindowsSessionSwitchTransitionResult transition =
            await WaitAfterLogoffAsync(sourceAccountId, targetAccountId, cancellationToken).ConfigureAwait(false);
        if (!transition.Succeeded)
        {
            return Failure(transition.ErrorCode, transition.Message);
        }

        if (transition.TargetAlreadyActive)
        {
            return Success("Expected managed Windows session became active during switch transition.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return FromLogon(await _logonService.LogonAsync(
            operationId,
            targetAccountId,
            cancellationToken).ConfigureAwait(false));
    }

    private async Task<WindowsSessionSwitchTransitionResult> WaitAfterLogoffAsync(
        string sourceAccountId,
        string targetAccountId,
        CancellationToken cancellationToken)
    {
        long startedAt = _clock.GetTimestamp();
        TimeSpan interval = _options.PostLogoffPollInterval <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(300)
            : _options.PostLogoffPollInterval;
        ProtoWindowsSessionState? lastLoggedState = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan elapsed = NonNegativeElapsed(startedAt);

            WindowsSessionStateServiceResult state =
                await _sessionStateService.GetStateAsync(cancellationToken).ConfigureAwait(false);
            if (!state.Succeeded)
            {
                return WindowsSessionSwitchTransitionResult.Failure(state.ErrorCode, state.Message);
            }

            if (lastLoggedState != state.State)
            {
                _logger.LogInformation(
                    "WINDOWS_SWITCH_WAIT_STATE state={State} elapsedMs={ElapsedMilliseconds}",
                    state.State,
                    (long)elapsed.TotalMilliseconds);
                lastLoggedState = state.State;
            }

            if (state.State == ProtoWindowsSessionState.NoSession)
            {
                _logger.LogInformation(
                    "WINDOWS_SWITCH_NO_SESSION_CONFIRMED elapsedMs={ElapsedMilliseconds}",
                    (long)elapsed.TotalMilliseconds);
                return WindowsSessionSwitchTransitionResult.NoSession();
            }

            if (StateMatchesAccount(state.State, targetAccountId))
            {
                return WindowsSessionSwitchTransitionResult.TargetActive();
            }

            if (state.State != ProtoWindowsSessionState.Unknown
                && !StateMatchesAccount(state.State, sourceAccountId))
            {
                return WindowsSessionSwitchTransitionResult.Failure(
                    NetworkOperationErrorCode.WindowsSessionChanged,
                    "Windows console session changed during switch.");
            }

            TimeSpan remaining = _options.PostLogoffWait - elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                _logger.LogWarning(
                    "WINDOWS_SWITCH_WAIT_TIMEOUT elapsedMs={ElapsedMilliseconds}",
                    (long)elapsed.TotalMilliseconds);
                return WindowsSessionSwitchTransitionResult.Failure(
                    NetworkOperationErrorCode.WindowsSwitchNotConfirmed,
                    "Windows session switch was not confirmed after logoff request.");
            }

            await _delay.DelayAsync(
                remaining < interval ? remaining : interval,
                cancellationToken).ConfigureAwait(false);
        }

        TimeSpan NonNegativeElapsed(long timestamp)
        {
            TimeSpan elapsed = _clock.GetElapsedTime(timestamp);
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }
    }

    private static bool StateMatchesAccount(ProtoWindowsSessionState state, string accountId)
    {
        return state switch
        {
            ProtoWindowsSessionState.PrimaryActive =>
                accountId == ClassroomManagedWindowsAccountTypes.Primary,
            ProtoWindowsSessionState.SecondaryActive =>
                accountId == ClassroomManagedWindowsAccountTypes.Secondary,
            _ => false
        };
    }

    private static string? AccountIdFromState(ProtoWindowsSessionState state)
    {
        return state switch
        {
            ProtoWindowsSessionState.PrimaryActive => ClassroomManagedWindowsAccountTypes.Primary,
            ProtoWindowsSessionState.SecondaryActive => ClassroomManagedWindowsAccountTypes.Secondary,
            _ => null
        };
    }

    private static WindowsSessionSwitchServiceResult FromLogon(WindowsSessionLogonServiceResult result)
    {
        return result.Succeeded
            ? Success(result.Message)
            : Failure(result.ErrorCode, result.Message);
    }

    private static WindowsSessionSwitchServiceResult Success(string message)
    {
        return WindowsSessionSwitchServiceResult.Success(message);
    }

    private static WindowsSessionSwitchServiceResult Failure(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return WindowsSessionSwitchServiceResult.Failure(errorCode, message);
    }

    private sealed record WindowsSessionSwitchTransitionResult(
        bool Succeeded,
        bool TargetAlreadyActive,
        NetworkOperationErrorCode ErrorCode,
        string Message)
    {
        public static WindowsSessionSwitchTransitionResult NoSession()
        {
            return new WindowsSessionSwitchTransitionResult(
                true,
                false,
                NetworkOperationErrorCode.Unspecified,
                string.Empty);
        }

        public static WindowsSessionSwitchTransitionResult TargetActive()
        {
            return new WindowsSessionSwitchTransitionResult(
                true,
                true,
                NetworkOperationErrorCode.Unspecified,
                string.Empty);
        }

        public static WindowsSessionSwitchTransitionResult Failure(
            NetworkOperationErrorCode errorCode,
            string message)
        {
            return new WindowsSessionSwitchTransitionResult(false, false, errorCode, message);
        }
    }
}

public sealed class SwitchManagedAccountOperationHandler : IRemoteOperationHandler
{
    private readonly WindowsSessionSwitchService _service;
    private readonly ILogger<SwitchManagedAccountOperationHandler> _logger;

    public SwitchManagedAccountOperationHandler(
        WindowsSessionSwitchService service,
        ILogger<SwitchManagedAccountOperationHandler> logger)
    {
        _service = service;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.SwitchManagedAccount;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.SwitchManagedAccount
            || request.SwitchManagedAccount is null)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "SWITCH_MANAGED_ACCOUNT parameters are required.");
        }

        string? targetAccountId = AccountIdFrom(request.SwitchManagedAccount.AccountId);
        if (targetAccountId is null)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "Managed Windows target accountId is invalid.");
        }

        WindowsSessionSwitchServiceResult result =
            await _service.SwitchAsync(request.OperationId, targetAccountId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Failed(result.ErrorCode, result.Message);
        }

        _logger.LogInformation(
            "Managed Windows account switch completed for target accountId {AccountId}.",
            targetAccountId);

        return RemoteOperationHandlerResult.Success(result.Message);
    }

    private static string? AccountIdFrom(ManagedWindowsAccountId accountId)
    {
        return accountId switch
        {
            ManagedWindowsAccountId.Primary => ClassroomManagedWindowsAccountTypes.Primary,
            ManagedWindowsAccountId.Secondary => ClassroomManagedWindowsAccountTypes.Secondary,
            _ => null
        };
    }

    private static RemoteOperationHandlerResult Failed(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Failed,
            errorCode,
            message);
    }
}
