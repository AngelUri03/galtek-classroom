using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Commands;

public sealed record ApplicationLaunchResult(
    bool Succeeded,
    string? Message)
{
    public static ApplicationLaunchResult Success()
    {
        return new ApplicationLaunchResult(true, null);
    }

    public static ApplicationLaunchResult Failed(string message)
    {
        return new ApplicationLaunchResult(false, message);
    }
}

public interface IWindowsApplicationLauncher
{
    Task<ApplicationLaunchResult> LaunchAsync(
        string executablePath,
        CancellationToken cancellationToken);
}

public sealed class UnavailableWindowsApplicationLauncher : IWindowsApplicationLauncher
{
    public Task<ApplicationLaunchResult> LaunchAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplicationLaunchResult.Failed("Application launch is unavailable."));
    }
}

public sealed record WindowsCreateProcessRequest(
    string ApplicationName,
    string? CommandLine,
    string WorkingDirectory,
    bool InheritHandles);

public sealed record WindowsCreateProcessResult(
    bool Succeeded,
    IntPtr ProcessHandle,
    IntPtr ThreadHandle,
    int ErrorCode);

public interface IWindowsProcessCreator
{
    WindowsCreateProcessResult CreateProcess(WindowsCreateProcessRequest request);
}

public interface IWindowsHandleCloser
{
    void Close(IntPtr handle);
}

public sealed class WindowsApplicationLauncher : IWindowsApplicationLauncher
{
    private readonly IWindowsProcessCreator _processCreator;
    private readonly IWindowsHandleCloser _handleCloser;

    [SupportedOSPlatform("windows")]
    public WindowsApplicationLauncher()
        : this(new NativeWindowsProcessCreator(), new NativeWindowsHandleCloser())
    {
    }

    public WindowsApplicationLauncher(
        IWindowsProcessCreator processCreator,
        IWindowsHandleCloser handleCloser)
    {
        _processCreator = processCreator;
        _handleCloser = handleCloser;
    }

    public Task<ApplicationLaunchResult> LaunchAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ApplicationBindingValidator.ValidateAbsoluteExePath(executablePath, requireExists: true);
        if (!validation.IsValid)
        {
            return Task.FromResult(ApplicationLaunchResult.Failed("Application executable is invalid or missing."));
        }

        var workingDirectory = Path.GetDirectoryName(validation.NormalizedValue);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Task.FromResult(ApplicationLaunchResult.Failed("Application working directory is invalid."));
        }

        var result = _processCreator.CreateProcess(new WindowsCreateProcessRequest(
            validation.NormalizedValue!,
            null,
            workingDirectory,
            InheritHandles: false));

        if (!result.Succeeded)
        {
            return Task.FromResult(ApplicationLaunchResult.Failed("Windows did not accept the application launch request."));
        }

        _handleCloser.Close(result.ProcessHandle);
        _handleCloser.Close(result.ThreadHandle);
        return Task.FromResult(ApplicationLaunchResult.Success());
    }
}

[SupportedOSPlatform("windows")]
public sealed class NativeWindowsProcessCreator : IWindowsProcessCreator
{
    public WindowsCreateProcessResult CreateProcess(WindowsCreateProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startupInfo = new StartupInfo
        {
            cb = Marshal.SizeOf<StartupInfo>()
        };

        var succeeded = CreateProcessW(
            request.ApplicationName,
            request.CommandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            request.InheritHandles,
            dwCreationFlags: 0,
            IntPtr.Zero,
            request.WorkingDirectory,
            ref startupInfo,
            out var processInformation);

        return new WindowsCreateProcessResult(
            succeeded,
            processInformation.hProcess,
            processInformation.hThread,
            succeeded ? 0 : Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
}

[SupportedOSPlatform("windows")]
public sealed class NativeWindowsHandleCloser : IWindowsHandleCloser
{
    public void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
