using System.Runtime.InteropServices;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.Power;

public sealed class WindowsPowerController : IWindowsPowerController
{
    private const uint CountdownSeconds = 10;
    private const bool ForceAppsClosed = false;
    private const string SystemMessage = "Galtek Classroom: operaci\u00f3n solicitada por la maestra.";

    private readonly ILogger<WindowsPowerController> _logger;

    public WindowsPowerController(ILogger<WindowsPowerController> logger)
    {
        _logger = logger;
    }

    public Task<WindowsPowerControlResult> RequestShutdownAsync(CancellationToken cancellationToken)
    {
        return RequestPowerTransitionAsync(rebootAfterShutdown: false, "shutdown", cancellationToken);
    }

    public Task<WindowsPowerControlResult> RequestRestartAsync(CancellationToken cancellationToken)
    {
        return RequestPowerTransitionAsync(rebootAfterShutdown: true, "restart", cancellationToken);
    }

    private Task<WindowsPowerControlResult> RequestPowerTransitionAsync(
        bool rebootAfterShutdown,
        string operationName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning(
                "Power control operation {OperationName} was requested on an unsupported platform.",
                operationName);

            return Task.FromResult(WindowsPowerControlResult.Failed(
                NetworkOperationErrorCode.PowerControlUnavailable,
                "Power control is not supported on this platform."));
        }

        try
        {
            NativeCallResult privilege = EnableShutdownPrivilege();
            if (!privilege.Succeeded)
            {
                _logger.LogWarning(
                    "SeShutdownPrivilege could not be enabled before {OperationName}. Step: {Step}; Win32Error: {Win32Error}",
                    operationName,
                    privilege.Step,
                    privilege.Win32Error);

                return Task.FromResult(WindowsPowerControlResult.Failed(
                    NetworkOperationErrorCode.PowerControlUnavailable,
                    "Windows shutdown privilege could not be enabled."));
            }

            if (!NativeMethods.InitiateSystemShutdownEx(
                null,
                SystemMessage,
                CountdownSeconds,
                ForceAppsClosed,
                rebootAfterShutdown,
                NativeMethods.ShutdownReasonMajorApplication | NativeMethods.ShutdownReasonFlagPlanned))
            {
                int win32Error = Marshal.GetLastWin32Error();
                _logger.LogWarning(
                    "Windows did not accept {OperationName} power control request. Win32Error: {Win32Error}",
                    operationName,
                    win32Error);

                return Task.FromResult(WindowsPowerControlResult.Failed(
                    NetworkOperationErrorCode.PowerControlFailed,
                    "Windows did not accept the power control request."));
            }

            _logger.LogInformation(
                "Windows accepted {OperationName} power control request with countdown {CountdownSeconds}s.",
                operationName,
                CountdownSeconds);

            return Task.FromResult(WindowsPowerControlResult.Success(
                rebootAfterShutdown
                    ? "Windows accepted the restart request."
                    : "Windows accepted the shutdown request."));
        }
        catch (DllNotFoundException exception)
        {
            _logger.LogWarning(
                exception,
                "Windows power control APIs were not available for {OperationName}.",
                operationName);

            return Task.FromResult(WindowsPowerControlResult.Failed(
                NetworkOperationErrorCode.PowerControlUnavailable,
                "Windows power control API is not available."));
        }
        catch (EntryPointNotFoundException exception)
        {
            _logger.LogWarning(
                exception,
                "Required Windows power control entry point was not available for {OperationName}.",
                operationName);

            return Task.FromResult(WindowsPowerControlResult.Failed(
                NetworkOperationErrorCode.PowerControlUnavailable,
                "Windows power control API is not available."));
        }
    }

    private static NativeCallResult EnableShutdownPrivilege()
    {
        if (!NativeMethods.OpenProcessToken(
            NativeMethods.GetCurrentProcess(),
            NativeMethods.TokenAdjustPrivileges | NativeMethods.TokenQuery,
            out nint tokenHandle))
        {
            return NativeCallResult.Failed("OpenProcessToken", Marshal.GetLastWin32Error());
        }

        try
        {
            if (!NativeMethods.LookupPrivilegeValue(
                null,
                NativeMethods.SeShutdownName,
                out NativeMethods.Luid luid))
            {
                return NativeCallResult.Failed("LookupPrivilegeValue", Marshal.GetLastWin32Error());
            }

            var privileges = new NativeMethods.TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeMethods.SePrivilegeEnabled
            };

            if (!NativeMethods.AdjustTokenPrivileges(
                tokenHandle,
                false,
                ref privileges,
                0,
                nint.Zero,
                nint.Zero))
            {
                return NativeCallResult.Failed("AdjustTokenPrivileges", Marshal.GetLastWin32Error());
            }

            int adjustError = Marshal.GetLastWin32Error();
            return adjustError == NativeMethods.ErrorNotAllAssigned
                ? NativeCallResult.Failed("AdjustTokenPrivileges", adjustError)
                : NativeCallResult.Success();
        }
        finally
        {
            _ = NativeMethods.CloseHandle(tokenHandle);
        }
    }

    private readonly record struct NativeCallResult(
        bool Succeeded,
        string Step,
        int Win32Error)
    {
        public static NativeCallResult Success()
        {
            return new NativeCallResult(true, string.Empty, 0);
        }

        public static NativeCallResult Failed(string step, int win32Error)
        {
            return new NativeCallResult(false, step, win32Error);
        }
    }

    private static partial class NativeMethods
    {
        public const uint TokenAdjustPrivileges = 0x0020;
        public const uint TokenQuery = 0x0008;
        public const uint SePrivilegeEnabled = 0x00000002;
        public const int ErrorNotAllAssigned = 1300;
        public const uint ShutdownReasonMajorApplication = 0x00040000;
        public const uint ShutdownReasonFlagPlanned = 0x80000000;
        public const string SeShutdownName = "SeShutdownPrivilege";

        [DllImport("kernel32.dll")]
        public static extern nint GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(
            nint processHandle,
            uint desiredAccess,
            out nint tokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LookupPrivilegeValue(
            string? systemName,
            string privilegeName,
            out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AdjustTokenPrivileges(
            nint tokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TokenPrivileges newState,
            uint bufferLength,
            nint previousState,
            nint returnLength);

        [DllImport("advapi32.dll", EntryPoint = "InitiateSystemShutdownExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitiateSystemShutdownEx(
            string? machineName,
            string? message,
            uint timeoutSeconds,
            [MarshalAs(UnmanagedType.Bool)] bool forceAppsClosed,
            [MarshalAs(UnmanagedType.Bool)] bool rebootAfterShutdown,
            uint reason);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint handle);

        [StructLayout(LayoutKind.Sequential)]
        public struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public Luid Luid;
            public uint Attributes;
        }
    }
}
