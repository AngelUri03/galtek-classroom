using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using GaltekClassroom.Agent.Shared;
using Microsoft.Win32.SafeHandles;

namespace GaltekClassroom.Agent.Service.CredentialProviderBridge;

public sealed record CredentialProviderCallerValidation(
    bool Authorized,
    int? ClientProcessId,
    string? ClientImagePath,
    string? ClientWindowsSid,
    string? ErrorMessage)
{
    public static CredentialProviderCallerValidation Allow(
        int clientProcessId,
        string clientImagePath,
        string clientWindowsSid)
    {
        return new CredentialProviderCallerValidation(
            true,
            clientProcessId,
            clientImagePath,
            clientWindowsSid,
            null);
    }

    public static CredentialProviderCallerValidation Deny(
        string errorMessage,
        int? clientProcessId = null,
        string? clientImagePath = null,
        string? clientWindowsSid = null)
    {
        return new CredentialProviderCallerValidation(
            false,
            clientProcessId,
            clientImagePath,
            clientWindowsSid,
            errorMessage);
    }
}

public sealed record CredentialProviderCallerProcessInfo(
    int ProcessId,
    int SessionId,
    string ImagePath);

public interface ICredentialProviderCallerPipeInspector
{
    bool IsWindows { get; }

    bool TryGetClientProcessId(PipeStream pipe, out int processId);

    string? TryGetClientSid(PipeStream pipe);
}

public interface ICredentialProviderCallerProcessInspector
{
    CredentialProviderCallerProcessInfo? TryGetProcess(int processId);
}

public interface ICredentialProviderCallerVerifier
{
    CredentialProviderCallerValidation Verify(PipeStream pipe);
}

public sealed class UnavailableCredentialProviderCallerVerifier : ICredentialProviderCallerVerifier
{
    public CredentialProviderCallerValidation Verify(PipeStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        return CredentialProviderCallerValidation.Deny(
            "Credential Provider bridge caller validation is only available on Windows.");
    }
}

public sealed class CredentialProviderCallerVerifier : ICredentialProviderCallerVerifier
{
    private readonly ICredentialProviderCallerPipeInspector _pipeInspector;
    private readonly ICredentialProviderCallerProcessInspector _processInspector;
    private readonly string _expectedLogonUiPath;

    public CredentialProviderCallerVerifier(
        ICredentialProviderCallerPipeInspector pipeInspector,
        ICredentialProviderCallerProcessInspector processInspector,
        string expectedLogonUiPath)
    {
        _pipeInspector = pipeInspector;
        _processInspector = processInspector;
        _expectedLogonUiPath = NormalizePath(expectedLogonUiPath);
    }

    public CredentialProviderCallerValidation Verify(PipeStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (!_pipeInspector.IsWindows)
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge caller validation is only supported on Windows.");
        }

        if (!_pipeInspector.TryGetClientProcessId(pipe, out var clientProcessId))
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client PID could not be resolved.");
        }

        var process = _processInspector.TryGetProcess(clientProcessId);
        if (process is null)
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client process could not be opened.",
                clientProcessId);
        }

        if (process.ProcessId != clientProcessId)
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client PID did not match the inspected process.",
                clientProcessId,
                process.ImagePath);
        }

        var normalizedImagePath = NormalizePath(process.ImagePath);
        if (!string.Equals(normalizedImagePath, _expectedLogonUiPath, StringComparison.OrdinalIgnoreCase))
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client image is not Windows LogonUI.",
                clientProcessId,
                process.ImagePath);
        }

        if (process.SessionId <= 0)
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client process is not in an interactive Windows session.",
                clientProcessId,
                process.ImagePath);
        }

        var clientSid = _pipeInspector.TryGetClientSid(pipe);
        if (!string.Equals(clientSid, CredentialProviderBridgeProtocol.LocalSystemSid, StringComparison.Ordinal))
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client token is not LocalSystem.",
                clientProcessId,
                process.ImagePath,
                clientSid);
        }

        return CredentialProviderCallerValidation.Allow(
            clientProcessId,
            process.ImagePath,
            clientSid!);
    }

    public static string DefaultExpectedLogonUiPath()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            windowsDirectory = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        }

        return Path.Combine(windowsDirectory, "System32", "LogonUI.exe");
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var trimmed = path.Trim();
        if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            trimmed = trimmed[4..];
        }

        return Path.GetFullPath(trimmed);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialProviderCallerPipeInspector : ICredentialProviderCallerPipeInspector
{
    public bool IsWindows => OperatingSystem.IsWindows();

    public bool TryGetClientProcessId(PipeStream pipe, out int processId)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        processId = 0;

        if (pipe is not NamedPipeServerStream serverStream
            || serverStream.SafePipeHandle.IsInvalid
            || serverStream.SafePipeHandle.IsClosed)
        {
            return false;
        }

        if (!GetNamedPipeClientProcessId(serverStream.SafePipeHandle, out var processIdRaw))
        {
            return false;
        }

        if (processIdRaw == 0 || processIdRaw > int.MaxValue)
        {
            return false;
        }

        processId = (int)processIdRaw;
        return true;
    }

    public string? TryGetClientSid(PipeStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (pipe is not NamedPipeServerStream serverStream)
        {
            return null;
        }

        try
        {
            string? windowsSid = null;
            serverStream.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                windowsSid = identity.User?.Value;
            });

            return windowsSid;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or SystemException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialProviderCallerProcessInspector : ICredentialProviderCallerProcessInspector
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public CredentialProviderCallerProcessInfo? TryGetProcess(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
        {
            return null;
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!ProcessIdToSessionId((uint)processId, out var sessionId))
            {
                return null;
            }

            var capacity = 32768;
            var builder = new System.Text.StringBuilder(capacity);
            var size = capacity;
            if (!QueryFullProcessImageName(handle, 0, builder, ref size) || size <= 0)
            {
                return null;
            }

            return new CredentialProviderCallerProcessInfo(
                processId,
                (int)sessionId,
                builder.ToString());
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or Win32Exception
            or NotSupportedException)
        {
            return null;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        int flags,
        System.Text.StringBuilder executablePath,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(
        uint processId,
        out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
