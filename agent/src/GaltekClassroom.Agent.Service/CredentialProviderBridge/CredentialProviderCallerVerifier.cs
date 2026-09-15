using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GaltekClassroom.Agent.Shared;
using Microsoft.Win32.SafeHandles;

namespace GaltekClassroom.Agent.Service.CredentialProviderBridge;

public static class CredentialProviderCallerValidationStages
{
    public const string Platform = "platform";
    public const string PipeStream = "pipe_stream";
    public const string PipeHandle = "pipe_handle";
    public const string Pid = "pid";
    public const string Process = "process";
    public const string Image = "image";
    public const string Session = "session";
    public const string Impersonation = "impersonation";
    public const string OpenThreadToken = "open_thread_token";
    public const string TokenUserLength = "token_user_length";
    public const string TokenUser = "token_user";
    public const string TokenSid = "token_sid";
    public const string RevertToSelf = "revert_to_self";
    public const string NotLocalSystem = "not_local_system";
    public const string Unknown = "unknown";
}

public sealed record CredentialProviderCallerValidation(
    bool Authorized,
    int? ClientProcessId,
    string? ClientImagePath,
    string? ClientWindowsSid,
    string? ErrorMessage,
    string? FailureStage,
    int? WindowsErrorCode)
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
            null,
            null,
            null);
    }

    public static CredentialProviderCallerValidation Deny(
        string errorMessage,
        string failureStage = CredentialProviderCallerValidationStages.Unknown,
        int? clientProcessId = null,
        string? clientImagePath = null,
        string? clientWindowsSid = null,
        int? windowsErrorCode = null)
    {
        return new CredentialProviderCallerValidation(
            false,
            clientProcessId,
            clientImagePath,
            clientWindowsSid,
            errorMessage,
            failureStage,
            windowsErrorCode);
    }
}

public sealed record CredentialProviderCallerProcessInfo(
    int ProcessId,
    int SessionId,
    string ImagePath);

public sealed record CredentialProviderCallerSidResult(
    bool Succeeded,
    string? WindowsSid,
    string? FailureStage,
    int? WindowsErrorCode)
{
    public static CredentialProviderCallerSidResult Success(string windowsSid)
    {
        return new CredentialProviderCallerSidResult(true, windowsSid, null, null);
    }

    public static CredentialProviderCallerSidResult Failure(string failureStage, int? windowsErrorCode = null)
    {
        return new CredentialProviderCallerSidResult(false, null, failureStage, windowsErrorCode);
    }
}

public interface ICredentialProviderCallerPipeInspector
{
    bool IsWindows { get; }

    bool TryGetClientProcessId(PipeStream pipe, out int processId);

    CredentialProviderCallerSidResult GetClientSid(PipeStream pipe);
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
            "Credential Provider bridge caller validation is only available on Windows.",
            CredentialProviderCallerValidationStages.Platform);
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
                "Credential Provider bridge caller validation is only supported on Windows.",
                CredentialProviderCallerValidationStages.Platform);
        }

        if (!_pipeInspector.TryGetClientProcessId(pipe, out var clientProcessId))
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client PID could not be resolved.",
                CredentialProviderCallerValidationStages.Pid);
        }

        var process = _processInspector.TryGetProcess(clientProcessId);
        if (process is null)
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client process could not be opened.",
                CredentialProviderCallerValidationStages.Process,
                clientProcessId);
        }

        if (process.ProcessId != clientProcessId)
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client PID did not match the inspected process.",
                CredentialProviderCallerValidationStages.Process,
                clientProcessId,
                process.ImagePath);
        }

        var normalizedImagePath = NormalizePath(process.ImagePath);
        if (!string.Equals(normalizedImagePath, _expectedLogonUiPath, StringComparison.OrdinalIgnoreCase))
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client image is not Windows LogonUI.",
                CredentialProviderCallerValidationStages.Image,
                clientProcessId,
                process.ImagePath);
        }

        if (process.SessionId <= 0)
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client process is not in an interactive Windows session.",
                CredentialProviderCallerValidationStages.Session,
                clientProcessId,
                process.ImagePath);
        }

        var clientSid = _pipeInspector.GetClientSid(pipe);
        if (!clientSid.Succeeded)
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client token could not be queried.",
                clientSid.FailureStage ?? CredentialProviderCallerValidationStages.Unknown,
                clientProcessId,
                process.ImagePath,
                windowsErrorCode: clientSid.WindowsErrorCode);
        }

        if (!string.Equals(clientSid.WindowsSid, CredentialProviderBridgeProtocol.LocalSystemSid, StringComparison.Ordinal))
        {
            return CredentialProviderCallerValidation.Deny(
                "Credential Provider bridge client token is not LocalSystem.",
                CredentialProviderCallerValidationStages.NotLocalSystem,
                clientProcessId,
                process.ImagePath,
                clientSid.WindowsSid);
        }

        return CredentialProviderCallerValidation.Allow(
            clientProcessId,
            process.ImagePath,
            clientSid.WindowsSid!);
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

public sealed class WindowsCredentialProviderCallerPipeInspector : ICredentialProviderCallerPipeInspector
{
    public const uint TokenQueryAccess = 0x0008;

    private const int TokenUserClass = 1;
    private const int ErrorInsufficientBuffer = 122;

    private readonly IWindowsCredentialProviderCallerPipeNative _native;

    public WindowsCredentialProviderCallerPipeInspector()
        : this(new WindowsCredentialProviderCallerPipeNative())
    {
    }

    public WindowsCredentialProviderCallerPipeInspector(IWindowsCredentialProviderCallerPipeNative native)
    {
        _native = native;
    }

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

    public CredentialProviderCallerSidResult GetClientSid(PipeStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (pipe is not NamedPipeServerStream serverStream)
        {
            return CredentialProviderCallerSidResult.Failure(
                CredentialProviderCallerValidationStages.PipeStream);
        }

        if (serverStream.SafePipeHandle.IsInvalid || serverStream.SafePipeHandle.IsClosed)
        {
            return CredentialProviderCallerSidResult.Failure(
                CredentialProviderCallerValidationStages.PipeHandle);
        }

        if (!_native.ImpersonateNamedPipeClient(serverStream.SafePipeHandle))
        {
            return CredentialProviderCallerSidResult.Failure(
                CredentialProviderCallerValidationStages.Impersonation,
                _native.GetLastWin32Error());
        }

        CredentialProviderCallerSidResult result;
        try
        {
            result = ReadImpersonatedTokenUserSid();
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or SystemException)
        {
            result = CredentialProviderCallerSidResult.Failure(
                CredentialProviderCallerValidationStages.TokenUser);
        }

        if (!_native.RevertToSelf())
        {
            return CredentialProviderCallerSidResult.Failure(
                CredentialProviderCallerValidationStages.RevertToSelf,
                _native.GetLastWin32Error());
        }

        return result;
    }

    private CredentialProviderCallerSidResult ReadImpersonatedTokenUserSid()
    {
        if (!_native.OpenThreadToken(
                _native.GetCurrentThread(),
                TokenQueryAccess,
                openAsSelf: true,
                out var tokenHandle))
        {
            return CredentialProviderCallerSidResult.Failure(
                CredentialProviderCallerValidationStages.OpenThreadToken,
                _native.GetLastWin32Error());
        }

        try
        {
            _ = _native.GetTokenInformation(
                tokenHandle,
                TokenUserClass,
                IntPtr.Zero,
                0,
                out var requiredLength);
            var lengthError = _native.GetLastWin32Error();
            if (requiredLength <= 0)
            {
                return CredentialProviderCallerSidResult.Failure(
                    CredentialProviderCallerValidationStages.TokenUserLength,
                    lengthError);
            }

            var buffer = Marshal.AllocHGlobal(requiredLength);
            try
            {
                if (!_native.GetTokenInformation(
                        tokenHandle,
                        TokenUserClass,
                        buffer,
                        requiredLength,
                        out _))
                {
                    return CredentialProviderCallerSidResult.Failure(
                        CredentialProviderCallerValidationStages.TokenUser,
                        _native.GetLastWin32Error());
                }

                var tokenUser = Marshal.PtrToStructure<TokenUser>(buffer);
                if (tokenUser.User.Sid == IntPtr.Zero)
                {
                    return CredentialProviderCallerSidResult.Failure(
                        CredentialProviderCallerValidationStages.TokenUser);
                }

                if (!_native.ConvertSidToStringSid(tokenUser.User.Sid, out var stringSid))
                {
                    return CredentialProviderCallerSidResult.Failure(
                        CredentialProviderCallerValidationStages.TokenSid,
                        _native.GetLastWin32Error());
                }

                try
                {
                    var windowsSid = Marshal.PtrToStringUni(stringSid);
                    return string.IsNullOrWhiteSpace(windowsSid)
                        ? CredentialProviderCallerSidResult.Failure(CredentialProviderCallerValidationStages.TokenSid)
                        : CredentialProviderCallerSidResult.Success(windowsSid);
                }
                finally
                {
                    _ = _native.LocalFree(stringSid);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = _native.CloseHandle(tokenHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SidAndAttributes
    {
        public readonly IntPtr Sid;
        public readonly int Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TokenUser
    {
        public readonly SidAndAttributes User;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}

public interface IWindowsCredentialProviderCallerPipeNative
{
    bool ImpersonateNamedPipeClient(SafePipeHandle pipe);

    bool RevertToSelf();

    IntPtr GetCurrentThread();

    bool OpenThreadToken(IntPtr thread, uint desiredAccess, bool openAsSelf, out IntPtr tokenHandle);

    bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid);

    IntPtr LocalFree(IntPtr memory);

    bool CloseHandle(IntPtr handle);

    int GetLastWin32Error();
}

public sealed class WindowsCredentialProviderCallerPipeNative : IWindowsCredentialProviderCallerPipeNative
{
    public bool ImpersonateNamedPipeClient(SafePipeHandle pipe)
    {
        return NativeImpersonateNamedPipeClient(pipe);
    }

    public bool RevertToSelf()
    {
        return NativeRevertToSelf();
    }

    public IntPtr GetCurrentThread()
    {
        return NativeGetCurrentThread();
    }

    public bool OpenThreadToken(IntPtr thread, uint desiredAccess, bool openAsSelf, out IntPtr tokenHandle)
    {
        return NativeOpenThreadToken(thread, desiredAccess, openAsSelf, out tokenHandle);
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

    public IntPtr LocalFree(IntPtr memory)
    {
        return NativeLocalFree(memory);
    }

    public bool CloseHandle(IntPtr handle)
    {
        return NativeCloseHandle(handle);
    }

    public int GetLastWin32Error()
    {
        return Marshal.GetLastWin32Error();
    }

    [DllImport("advapi32.dll", EntryPoint = "ImpersonateNamedPipeClient", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeImpersonateNamedPipeClient(SafePipeHandle pipe);

    [DllImport("advapi32.dll", EntryPoint = "RevertToSelf", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeRevertToSelf();

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThread")]
    private static extern IntPtr NativeGetCurrentThread();

    [DllImport("advapi32.dll", EntryPoint = "OpenThreadToken", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeOpenThreadToken(
        IntPtr thread,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool openAsSelf,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeGetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "ConvertSidToStringSidW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeConvertSidToStringSid(
        IntPtr sid,
        out IntPtr stringSid);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr NativeLocalFree(IntPtr memory);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeCloseHandle(IntPtr handle);
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
