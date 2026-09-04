using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using ProtoWindowsSessionState = GaltekClassroom.Protocol.Network.V1.WindowsSessionState;

namespace GaltekClassroom.Agent.Service.WindowsSessions;

public enum ConsoleSessionIdentityObservationStatus
{
    NoSession,
    User,
    Unknown,
    Failed
}

public sealed record ConsoleSessionIdentityObservation(
    ConsoleSessionIdentityObservationStatus Status,
    uint? SessionId,
    string? WindowsSid,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public bool Succeeded => Status != ConsoleSessionIdentityObservationStatus.Failed;

    public static ConsoleSessionIdentityObservation NoSession(uint sessionId)
    {
        return new ConsoleSessionIdentityObservation(
            ConsoleSessionIdentityObservationStatus.NoSession,
            sessionId,
            null,
            NetworkOperationErrorCode.Unspecified,
            string.Empty);
    }

    public static ConsoleSessionIdentityObservation User(uint sessionId, string windowsSid)
    {
        return new ConsoleSessionIdentityObservation(
            ConsoleSessionIdentityObservationStatus.User,
            sessionId,
            windowsSid,
            NetworkOperationErrorCode.Unspecified,
            string.Empty);
    }

    public static ConsoleSessionIdentityObservation Unknown(uint? sessionId, string message)
    {
        return new ConsoleSessionIdentityObservation(
            ConsoleSessionIdentityObservationStatus.Unknown,
            sessionId,
            null,
            NetworkOperationErrorCode.Unspecified,
            message);
    }

    public static ConsoleSessionIdentityObservation Failed(string message)
    {
        return new ConsoleSessionIdentityObservation(
            ConsoleSessionIdentityObservationStatus.Failed,
            null,
            null,
            NetworkOperationErrorCode.WindowsSessionUnknown,
            message);
    }
}

public interface IWindowsConsoleSessionResolver
{
    Task<ConsoleSessionIdentityObservation> ObserveAsync(CancellationToken cancellationToken);
}

public interface IWindowsPrivilegeLease : IDisposable
{
}

public interface IWindowsConsoleSessionNativeApi
{
    bool IsWindows { get; }

    uint GetActiveConsoleSessionId();

    bool IsRunningAsLocalSystem();

    bool TryEnableTcbPrivilege(out IWindowsPrivilegeLease? lease, out string step, out int win32Error);

    bool WtsQuerySessionInformation(
        uint sessionId,
        int infoClass,
        out IntPtr buffer,
        out int bytesReturned);

    void WtsFreeMemory(IntPtr buffer);

    bool WtsQueryUserToken(uint sessionId, out IntPtr tokenHandle);

    bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid);

    string? PtrToStringUni(IntPtr pointer);

    IntPtr AllocHGlobal(int bytes);

    void FreeHGlobal(IntPtr buffer);

    void LocalFree(IntPtr buffer);

    void CloseHandle(IntPtr handle);
}

public sealed class WindowsConsoleSessionResolver : IWindowsConsoleSessionResolver
{
    public const uint WtsNoSession = 0xFFFFFFFF;
    public const int WtsUserName = 5;
    public const int TokenUserClass = 1;

    private readonly IWindowsConsoleSessionNativeApi _native;
    private readonly ILogger<WindowsConsoleSessionResolver> _logger;

    public WindowsConsoleSessionResolver(
        IWindowsConsoleSessionNativeApi native,
        ILogger<WindowsConsoleSessionResolver> logger)
    {
        _native = native;
        _logger = logger;
    }

    public Task<ConsoleSessionIdentityObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_native.IsWindows)
        {
            return Task.FromResult(ConsoleSessionIdentityObservation.Failed(
                "Windows session state is supported only on Windows."));
        }

        uint sessionId = _native.GetActiveConsoleSessionId();
        if (sessionId == WtsNoSession)
        {
            return Task.FromResult(ConsoleSessionIdentityObservation.Unknown(
                sessionId,
                "Windows console session is temporarily unavailable."));
        }

        if (sessionId == 0)
        {
            return Task.FromResult(ConsoleSessionIdentityObservation.Unknown(
                sessionId,
                "Windows console session resolved to Session 0."));
        }

        if (!TryQueryUserPresence(sessionId, out bool userPresent))
        {
            return Task.FromResult(ConsoleSessionIdentityObservation.Failed(
                "Windows console session user presence could not be determined."));
        }

        if (!userPresent)
        {
            return Task.FromResult(ConsoleSessionIdentityObservation.NoSession(sessionId));
        }

        if (!_native.IsRunningAsLocalSystem())
        {
            return Task.FromResult(ConsoleSessionIdentityObservation.Failed(
                "Agent Service is not running as LocalSystem."));
        }

        if (!_native.TryEnableTcbPrivilege(out IWindowsPrivilegeLease? privilege, out string step, out int win32Error)
            || privilege is null)
        {
            _logger.LogWarning(
                "SeTcbPrivilege could not be enabled for Windows console session state. Step: {Step}; Win32Error: {Win32Error}",
                step,
                win32Error);

            privilege?.Dispose();
            return Task.FromResult(ConsoleSessionIdentityObservation.Failed(
                "Windows session token privilege could not be enabled."));
        }

        using (privilege)
        {
            if (!_native.WtsQueryUserToken(sessionId, out IntPtr tokenHandle) || tokenHandle == IntPtr.Zero)
            {
                return Task.FromResult(ConsoleSessionIdentityObservation.Failed(
                    "Windows console user token could not be opened."));
            }

            try
            {
                return Task.FromResult(ReadTokenUserSid(sessionId, tokenHandle));
            }
            finally
            {
                _native.CloseHandle(tokenHandle);
            }
        }
    }

    private bool TryQueryUserPresence(uint sessionId, out bool userPresent)
    {
        userPresent = false;

        if (!_native.WtsQuerySessionInformation(sessionId, WtsUserName, out IntPtr buffer, out _))
        {
            return false;
        }

        try
        {
            string username = buffer == IntPtr.Zero
                ? string.Empty
                : _native.PtrToStringUni(buffer) ?? string.Empty;
            userPresent = !string.IsNullOrWhiteSpace(username);
            return true;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                _native.WtsFreeMemory(buffer);
            }
        }
    }

    private ConsoleSessionIdentityObservation ReadTokenUserSid(uint sessionId, IntPtr tokenHandle)
    {
        _ = _native.GetTokenInformation(
            tokenHandle,
            TokenUserClass,
            IntPtr.Zero,
            0,
            out int requiredLength);

        if (requiredLength <= 0)
        {
            return ConsoleSessionIdentityObservation.Failed(
                "Windows console user SID length could not be determined.");
        }

        IntPtr buffer = _native.AllocHGlobal(requiredLength);
        try
        {
            if (!_native.GetTokenInformation(
                tokenHandle,
                TokenUserClass,
                buffer,
                requiredLength,
                out _))
            {
                return ConsoleSessionIdentityObservation.Failed(
                    "Windows console user SID could not be read.");
            }

            var tokenUser = Marshal.PtrToStructure<TokenUser>(buffer);
            if (tokenUser.User.Sid == IntPtr.Zero)
            {
                return ConsoleSessionIdentityObservation.Failed(
                    "Windows console user SID was empty.");
            }

            if (!_native.ConvertSidToStringSid(tokenUser.User.Sid, out IntPtr stringSid)
                || stringSid == IntPtr.Zero)
            {
                return ConsoleSessionIdentityObservation.Failed(
                    "Windows console user SID could not be converted.");
            }

            try
            {
                string? windowsSid = _native.PtrToStringUni(stringSid);
                if (!MasterBindingValidator.IsValidSid(windowsSid))
                {
                    return ConsoleSessionIdentityObservation.Failed(
                        "Windows console user SID was invalid.");
                }

                return ConsoleSessionIdentityObservation.User(sessionId, windowsSid!);
            }
            finally
            {
                _native.LocalFree(stringSid);
            }
        }
        finally
        {
            _native.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SidAndAttributes
    {
        public readonly IntPtr Sid;
        public readonly int Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct TokenUser
    {
        public readonly SidAndAttributes User;
    }
}

public sealed record WindowsSessionStateServiceResult(
    bool Succeeded,
    ProtoWindowsSessionState State,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static WindowsSessionStateServiceResult Success(ProtoWindowsSessionState state)
    {
        return new WindowsSessionStateServiceResult(
            true,
            state,
            NetworkOperationErrorCode.Unspecified,
            string.Empty);
    }

    public static WindowsSessionStateServiceResult Failure(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new WindowsSessionStateServiceResult(false, ProtoWindowsSessionState.Unknown, errorCode, message);
    }
}

public sealed class WindowsSessionStateService
{
    private readonly IWindowsConsoleSessionResolver _resolver;
    private readonly InstallationIdentityStore _installationIdentityStore;
    private readonly IManagedWindowsAccountBindingStore _bindingStore;

    public WindowsSessionStateService(
        IWindowsConsoleSessionResolver resolver,
        InstallationIdentityStore installationIdentityStore,
        IManagedWindowsAccountBindingStore bindingStore)
    {
        _resolver = resolver;
        _installationIdentityStore = installationIdentityStore;
        _bindingStore = bindingStore;
    }

    public async Task<WindowsSessionStateServiceResult> GetStateAsync(CancellationToken cancellationToken)
    {
        ConsoleSessionIdentityObservation observation =
            await _resolver.ObserveAsync(cancellationToken).ConfigureAwait(false);

        if (!observation.Succeeded)
        {
            return WindowsSessionStateServiceResult.Failure(
                observation.ErrorCode == NetworkOperationErrorCode.Unspecified
                    ? NetworkOperationErrorCode.WindowsSessionUnknown
                    : observation.ErrorCode,
                observation.Message);
        }

        if (observation.Status == ConsoleSessionIdentityObservationStatus.Unknown)
        {
            return await ValidateBindingsThenReturnAsync(
                ProtoWindowsSessionState.Unknown,
                cancellationToken).ConfigureAwait(false);
        }

        InstallationIdentityStoreReadResult identity =
            await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
        {
            return WindowsSessionStateServiceResult.Failure(
                NetworkOperationErrorCode.WindowsSessionUnknown,
                identity.ErrorMessage ?? "Installation identity is invalid.");
        }

        ManagedWindowsAccountBindingStoreReadResult bindings =
            await _bindingStore.LoadAsync(identity.Identity.InstallationId, cancellationToken).ConfigureAwait(false);
        if (!bindings.Loaded)
        {
            return WindowsSessionStateServiceResult.Failure(
                NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                bindings.ErrorMessage ?? "Managed Windows account bindings are invalid.");
        }

        if (observation.Status == ConsoleSessionIdentityObservationStatus.NoSession)
        {
            return WindowsSessionStateServiceResult.Success(ProtoWindowsSessionState.NoSession);
        }

        if (string.IsNullOrWhiteSpace(observation.WindowsSid))
        {
            return WindowsSessionStateServiceResult.Failure(
                NetworkOperationErrorCode.WindowsSessionUnknown,
                "Windows console session identity was not available.");
        }

        return WindowsSessionStateServiceResult.Success(MapSid(observation.WindowsSid, bindings.Bindings));
    }

    private async Task<WindowsSessionStateServiceResult> ValidateBindingsThenReturnAsync(
        ProtoWindowsSessionState state,
        CancellationToken cancellationToken)
    {
        InstallationIdentityStoreReadResult identity =
            await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
        {
            return WindowsSessionStateServiceResult.Failure(
                NetworkOperationErrorCode.WindowsSessionUnknown,
                identity.ErrorMessage ?? "Installation identity is invalid.");
        }

        ManagedWindowsAccountBindingStoreReadResult bindings =
            await _bindingStore.LoadAsync(identity.Identity.InstallationId, cancellationToken).ConfigureAwait(false);
        return bindings.Loaded
            ? WindowsSessionStateServiceResult.Success(state)
            : WindowsSessionStateServiceResult.Failure(
                NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                bindings.ErrorMessage ?? "Managed Windows account bindings are invalid.");
    }

    private static ProtoWindowsSessionState MapSid(
        string activeWindowsSid,
        IReadOnlyList<ManagedWindowsAccountBinding> bindings)
    {
        foreach (ManagedWindowsAccountBinding binding in bindings)
        {
            if (!string.Equals(binding.WindowsSid, activeWindowsSid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return binding.AccountId switch
            {
                ClassroomManagedWindowsAccountTypes.Primary => ProtoWindowsSessionState.PrimaryActive,
                ClassroomManagedWindowsAccountTypes.Secondary => ProtoWindowsSessionState.SecondaryActive,
                _ => ProtoWindowsSessionState.OtherSessionActive
            };
        }

        return ProtoWindowsSessionState.OtherSessionActive;
    }
}

public sealed class GetWindowsSessionStateOperationHandler : IRemoteOperationHandler
{
    private readonly WindowsSessionStateService _service;
    private readonly ILogger<GetWindowsSessionStateOperationHandler> _logger;

    public GetWindowsSessionStateOperationHandler(
        WindowsSessionStateService service,
        ILogger<GetWindowsSessionStateOperationHandler> logger)
    {
        _service = service;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.GetWindowsSessionState;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.None)
        {
            return new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                NetworkOperationErrorCode.ProtocolViolation,
                "GET_WINDOWS_SESSION_STATE does not accept operation parameters.");
        }

        WindowsSessionStateServiceResult state =
            await _service.GetStateAsync(cancellationToken).ConfigureAwait(false);

        if (!state.Succeeded)
        {
            return new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                state.ErrorCode,
                state.Message);
        }

        _logger.LogInformation(
            "Observed Windows session state. OperationId: {OperationId}; TargetDeviceId: {TargetDeviceId}; State: {State}",
            request.OperationId,
            request.TargetDeviceId,
            state.State);

        return RemoteOperationHandlerResult.Success(
            "Windows session state was observed.",
            new WindowsSessionStateResult
            {
                State = state.State
            });
    }
}

public sealed class WindowsConsoleSessionNativeApi : IWindowsConsoleSessionNativeApi
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;
    private const string SeTcbName = "SeTcbPrivilege";

    public bool IsWindows => OperatingSystem.IsWindows();

    public uint GetActiveConsoleSessionId()
    {
        return WTSGetActiveConsoleSessionId();
    }

    [SupportedOSPlatform("windows")]
    public bool IsRunningAsLocalSystem()
    {
        return System.Security.Principal.WindowsIdentity.GetCurrent().IsSystem;
    }

    public bool TryEnableTcbPrivilege(out IWindowsPrivilegeLease? lease, out string step, out int win32Error)
    {
        lease = null;
        step = string.Empty;
        win32Error = 0;

        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out IntPtr tokenHandle))
        {
            step = "OpenProcessToken";
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, SeTcbName, out Luid luid))
            {
                step = "LookupPrivilegeValue";
                win32Error = Marshal.GetLastWin32Error();
                CloseHandle(tokenHandle);
                return false;
            }

            var newState = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled
            };
            var previousState = new TokenPrivileges();

            if (!AdjustTokenPrivileges(
                tokenHandle,
                false,
                ref newState,
                (uint)Marshal.SizeOf<TokenPrivileges>(),
                ref previousState,
                out uint returnLength))
            {
                step = "AdjustTokenPrivileges";
                win32Error = Marshal.GetLastWin32Error();
                CloseHandle(tokenHandle);
                return false;
            }

            int adjustError = Marshal.GetLastWin32Error();
            if (adjustError == ErrorNotAllAssigned)
            {
                step = "AdjustTokenPrivileges";
                win32Error = adjustError;
                CloseHandle(tokenHandle);
                return false;
            }

            lease = new WindowsPrivilegeLease(tokenHandle, previousState, returnLength > 0);
            return true;
        }
        catch
        {
            CloseHandle(tokenHandle);
            throw;
        }
    }

    public bool WtsQuerySessionInformation(
        uint sessionId,
        int infoClass,
        out IntPtr buffer,
        out int bytesReturned)
    {
        return WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out buffer, out bytesReturned);
    }

    public void WtsFreeMemory(IntPtr buffer)
    {
        WTSFreeMemory(buffer);
    }

    public bool WtsQueryUserToken(uint sessionId, out IntPtr tokenHandle)
    {
        return WTSQueryUserToken(sessionId, out tokenHandle);
    }

    public bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength)
    {
        return NativeGetTokenInformation(
            tokenHandle,
            tokenInformationClass,
            tokenInformation,
            tokenInformationLength,
            out returnLength);
    }

    public bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid)
    {
        return NativeConvertSidToStringSid(sid, out stringSid);
    }

    public string? PtrToStringUni(IntPtr pointer)
    {
        return Marshal.PtrToStringUni(pointer);
    }

    public IntPtr AllocHGlobal(int bytes)
    {
        return Marshal.AllocHGlobal(bytes);
    }

    public void FreeHGlobal(IntPtr buffer)
    {
        Marshal.FreeHGlobal(buffer);
    }

    public void LocalFree(IntPtr buffer)
    {
        _ = NativeLocalFree(buffer);
    }

    public void CloseHandle(IntPtr handle)
    {
        _ = NativeCloseHandle(handle);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr serverHandle,
        uint sessionId,
        int infoClass,
        out IntPtr buffer,
        out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr buffer);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(
        string? systemName,
        string privilegeName,
        out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        ref TokenPrivileges previousState,
        out uint returnLength);

    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeGetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeConvertSidToStringSid(
        IntPtr sid,
        out IntPtr stringSid);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree", SetLastError = true)]
    private static extern IntPtr NativeLocalFree(IntPtr buffer);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeCloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    private sealed class WindowsPrivilegeLease : IWindowsPrivilegeLease
    {
        private IntPtr _tokenHandle;
        private TokenPrivileges _previousState;
        private readonly bool _restorePreviousState;

        public WindowsPrivilegeLease(
            IntPtr tokenHandle,
            TokenPrivileges previousState,
            bool restorePreviousState)
        {
            _tokenHandle = tokenHandle;
            _previousState = previousState;
            _restorePreviousState = restorePreviousState;
        }

        public void Dispose()
        {
            if (_tokenHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (_restorePreviousState)
                {
                    _ = AdjustTokenPrivileges(
                        _tokenHandle,
                        false,
                        ref _previousState,
                        0,
                        ref _previousState,
                        out _);
                }
            }
            finally
            {
                _ = NativeCloseHandle(_tokenHandle);
                _tokenHandle = IntPtr.Zero;
            }
        }
    }
}
