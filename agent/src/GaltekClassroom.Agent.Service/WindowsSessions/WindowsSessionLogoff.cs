using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.WindowsSessions;

public sealed record WindowsSessionLogoffServiceResult(
    bool Succeeded,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static WindowsSessionLogoffServiceResult Success(string message)
    {
        return new WindowsSessionLogoffServiceResult(true, NetworkOperationErrorCode.Unspecified, message);
    }

    public static WindowsSessionLogoffServiceResult Failure(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new WindowsSessionLogoffServiceResult(false, errorCode, message);
    }
}

public interface IWindowsSessionLogoffController
{
    bool Logoff(uint sessionId, out int win32Error);
}

public interface IWindowsSessionLogoffNativeApi
{
    bool WtsLogoffSession(IntPtr serverHandle, uint sessionId, bool wait);

    int GetLastWin32Error();
}

public sealed class WindowsSessionLogoffService
{
    private readonly IWindowsConsoleSessionResolver _resolver;
    private readonly IWindowsSessionLogoffController _logoffController;
    private readonly InstallationIdentityStore _installationIdentityStore;
    private readonly IManagedWindowsAccountBindingStore _bindingStore;
    private readonly ILogger<WindowsSessionLogoffService> _logger;

    public WindowsSessionLogoffService(
        IWindowsConsoleSessionResolver resolver,
        IWindowsSessionLogoffController logoffController,
        InstallationIdentityStore installationIdentityStore,
        IManagedWindowsAccountBindingStore bindingStore,
        ILogger<WindowsSessionLogoffService> logger)
    {
        _resolver = resolver;
        _logoffController = logoffController;
        _installationIdentityStore = installationIdentityStore;
        _bindingStore = bindingStore;
        _logger = logger;
    }

    public async Task<WindowsSessionLogoffServiceResult> LogoffAsync(
        string expectedAccountId,
        CancellationToken cancellationToken)
    {
        ManagedWindowsAccountBinding? expectedBinding;
        try
        {
            expectedBinding = await LoadExpectedBindingAsync(expectedAccountId, cancellationToken).ConfigureAwait(false);
        }
        catch (WindowsSessionLogoffConfigurationException exception)
        {
            return Failure(exception.ErrorCode, exception.Message);
        }
        if (expectedBinding is null)
        {
            return Failure(NetworkOperationErrorCode.AccountNotConfigured, "Managed Windows account is not configured.");
        }

        ConsoleSessionIdentityObservation first =
            await _resolver.ObserveAsync(cancellationToken).ConfigureAwait(false);
        WindowsSessionLogoffServiceResult? firstDecision = ValidateObservationForExpectedAccount(
            first,
            expectedBinding,
            allowNoSessionSuccess: true);
        if (firstDecision is not null)
        {
            return firstDecision;
        }

        ConsoleSessionIdentityObservation second =
            await _resolver.ObserveAsync(cancellationToken).ConfigureAwait(false);
        WindowsSessionLogoffServiceResult? secondDecision = ValidateSecondObservation(
            first,
            second,
            expectedBinding);
        if (secondDecision is not null)
        {
            return secondDecision;
        }

        uint sessionId = second.SessionId!.Value;
        if (!_logoffController.Logoff(sessionId, out int win32Error))
        {
            _logger.LogWarning(
                "Windows logoff request failed for managed account {AccountId}. Win32Error: {Win32Error}",
                expectedBinding.AccountId,
                win32Error);

            return Failure(
                NetworkOperationErrorCode.WindowsLogoffFailed,
                "Windows did not accept the logoff request.");
        }

        return Success("Windows accepted the logoff request.");
    }

    private async Task<ManagedWindowsAccountBinding?> LoadExpectedBindingAsync(
        string expectedAccountId,
        CancellationToken cancellationToken)
    {
        InstallationIdentityStoreReadResult identity =
            await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
        {
            throw new WindowsSessionLogoffConfigurationException(
                NetworkOperationErrorCode.WindowsSessionUnknown,
                identity.ErrorMessage ?? "Installation identity is invalid.");
        }

        ManagedWindowsAccountBindingStoreReadResult bindings =
            await _bindingStore.GetAsync(
                identity.Identity.InstallationId,
                expectedAccountId,
                cancellationToken).ConfigureAwait(false);
        if (!bindings.Loaded)
        {
            throw new WindowsSessionLogoffConfigurationException(
                NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                bindings.ErrorMessage ?? "Managed Windows account bindings are invalid.");
        }

        return bindings.Bindings.Count == 0 ? null : bindings.Bindings[0];
    }

    private static WindowsSessionLogoffServiceResult? ValidateObservationForExpectedAccount(
        ConsoleSessionIdentityObservation observation,
        ManagedWindowsAccountBinding expectedBinding,
        bool allowNoSessionSuccess)
    {
        if (!observation.Succeeded)
        {
            return Failure(
                observation.ErrorCode == NetworkOperationErrorCode.Unspecified
                    ? NetworkOperationErrorCode.WindowsSessionUnknown
                    : observation.ErrorCode,
                observation.Message);
        }

        if (observation.Status == ConsoleSessionIdentityObservationStatus.Unknown)
        {
            return Failure(
                NetworkOperationErrorCode.WindowsSessionUnknown,
                "Windows console session identity could not be determined.");
        }

        if (observation.Status == ConsoleSessionIdentityObservationStatus.NoSession)
        {
            return allowNoSessionSuccess
                ? Success("Expected managed Windows session is already absent.")
                : Failure(
                    NetworkOperationErrorCode.WindowsSessionChanged,
                    "Windows console session changed before logoff.");
        }

        if (string.IsNullOrWhiteSpace(observation.WindowsSid)
            || !string.Equals(
                observation.WindowsSid,
                expectedBinding.WindowsSid,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                NetworkOperationErrorCode.WindowsSessionChanged,
                "Windows console session does not match the expected managed account.");
        }

        return null;
    }

    private static WindowsSessionLogoffServiceResult? ValidateSecondObservation(
        ConsoleSessionIdentityObservation first,
        ConsoleSessionIdentityObservation second,
        ManagedWindowsAccountBinding expectedBinding)
    {
        WindowsSessionLogoffServiceResult? secondDecision = ValidateObservationForExpectedAccount(
            second,
            expectedBinding,
            allowNoSessionSuccess: true);
        if (secondDecision is not null)
        {
            return secondDecision;
        }

        if (first.SessionId != second.SessionId)
        {
            return Failure(
                NetworkOperationErrorCode.WindowsSessionChanged,
                "Windows console session changed before logoff.");
        }

        return null;
    }

    private static WindowsSessionLogoffServiceResult Success(string message)
    {
        return WindowsSessionLogoffServiceResult.Success(message);
    }

    private static WindowsSessionLogoffServiceResult Failure(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return WindowsSessionLogoffServiceResult.Failure(errorCode, message);
    }

    public sealed class WindowsSessionLogoffConfigurationException : Exception
    {
        public WindowsSessionLogoffConfigurationException(
            NetworkOperationErrorCode errorCode,
            string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public NetworkOperationErrorCode ErrorCode { get; }
    }
}

public sealed class LogoffWindowsSessionOperationHandler : IRemoteOperationHandler
{
    private readonly WindowsSessionLogoffService _service;
    private readonly ILogger<LogoffWindowsSessionOperationHandler> _logger;

    public LogoffWindowsSessionOperationHandler(
        WindowsSessionLogoffService service,
        ILogger<LogoffWindowsSessionOperationHandler> logger)
    {
        _service = service;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.LogoffWindowsSession;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.LogoffWindowsSession
            || request.LogoffWindowsSession is null)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "LOGOFF_WINDOWS_SESSION parameters are required.");
        }

        string? accountId = AccountIdFrom(request.LogoffWindowsSession.AccountId);
        if (accountId is null)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "Managed Windows accountId is invalid.");
        }

        WindowsSessionLogoffServiceResult result;
        try
        {
            result = await _service.LogoffAsync(accountId, cancellationToken).ConfigureAwait(false);
        }
        catch (WindowsSessionLogoffService.WindowsSessionLogoffConfigurationException exception)
        {
            return Failed(exception.ErrorCode, exception.Message);
        }

        if (!result.Succeeded)
        {
            return Failed(result.ErrorCode, result.Message);
        }

        _logger.LogInformation(
            "Windows logoff request completed for managed account {AccountId}.",
            accountId);

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

public sealed class WindowsSessionLogoffController : IWindowsSessionLogoffController
{
    private readonly IWindowsSessionLogoffNativeApi _native;

    public WindowsSessionLogoffController(IWindowsSessionLogoffNativeApi native)
    {
        _native = native;
    }

    public bool Logoff(uint sessionId, out int win32Error)
    {
        if (_native.WtsLogoffSession(IntPtr.Zero, sessionId, wait: false))
        {
            win32Error = 0;
            return true;
        }

        win32Error = _native.GetLastWin32Error();
        return false;
    }
}

public sealed class WindowsSessionLogoffNativeApi : IWindowsSessionLogoffNativeApi
{
    public bool WtsLogoffSession(IntPtr serverHandle, uint sessionId, bool wait)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return NativeWtsLogoffSession(serverHandle, sessionId, wait);
    }

    public int GetLastWin32Error()
    {
        return Marshal.GetLastWin32Error();
    }

    [SupportedOSPlatform("windows")]
    [DllImport("wtsapi32.dll", EntryPoint = "WTSLogoffSession", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeWtsLogoffSession(
        IntPtr serverHandle,
        uint sessionId,
        [MarshalAs(UnmanagedType.Bool)] bool wait);
}
