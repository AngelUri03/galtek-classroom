using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Commands;

public sealed record UrlLaunchResult(
    bool Succeeded,
    string? Message)
{
    public static UrlLaunchResult Success()
    {
        return new UrlLaunchResult(true, null);
    }

    public static UrlLaunchResult Failed(string message)
    {
        return new UrlLaunchResult(false, message);
    }
}

public interface IUrlLauncher
{
    Task<UrlLaunchResult> LaunchAsync(string url, CancellationToken cancellationToken);
}

public sealed class UnavailableUrlLauncher : IUrlLauncher
{
    public Task<UrlLaunchResult> LaunchAsync(string url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(UrlLaunchResult.Failed("URL launch is unavailable."));
    }
}

public sealed record WindowsShellExecuteRequest(
    string Verb,
    string File,
    string? Parameters);

public sealed record WindowsShellExecuteResult(
    bool Succeeded,
    int ErrorCode);

public interface IWindowsShellExecutor
{
    WindowsShellExecuteResult Execute(WindowsShellExecuteRequest request);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsUrlLauncher : IUrlLauncher
{
    private readonly IWindowsShellExecutor _shellExecutor;
    private readonly OpenUrlSafetyPolicy _openUrlSafetyPolicy;

    public WindowsUrlLauncher()
        : this(new NativeWindowsShellExecutor(), new OpenUrlSafetyPolicy())
    {
    }

    public WindowsUrlLauncher(
        IWindowsShellExecutor shellExecutor,
        OpenUrlSafetyPolicy? openUrlSafetyPolicy = null)
    {
        _shellExecutor = shellExecutor;
        _openUrlSafetyPolicy = openUrlSafetyPolicy ?? new OpenUrlSafetyPolicy();
    }

    public Task<UrlLaunchResult> LaunchAsync(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = _openUrlSafetyPolicy.Validate(url);
        if (!validation.IsValid)
        {
            return Task.FromResult(UrlLaunchResult.Failed("URL is invalid."));
        }

        WindowsShellExecuteResult result = _shellExecutor.Execute(new WindowsShellExecuteRequest(
            "open",
            url,
            null));

        return Task.FromResult(result.Succeeded
            ? UrlLaunchResult.Success()
            : UrlLaunchResult.Failed("Windows did not accept the URL launch request."));
    }
}

[SupportedOSPlatform("windows")]
public sealed class NativeWindowsShellExecutor : IWindowsShellExecutor
{
    private const int ShowNormal = 1;

    public WindowsShellExecuteResult Execute(WindowsShellExecuteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executeInfo = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            lpVerb = request.Verb,
            lpFile = request.File,
            lpParameters = request.Parameters,
            nShow = ShowNormal
        };

        var succeeded = ShellExecuteExW(ref executeInfo);
        return new WindowsShellExecuteResult(
            succeeded,
            succeeded ? 0 : Marshal.GetLastWin32Error());
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo lpExecInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }
}
