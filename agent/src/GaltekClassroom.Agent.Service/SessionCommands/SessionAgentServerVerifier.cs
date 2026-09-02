using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using GaltekClassroom.Agent.Shared;
using Microsoft.Win32.SafeHandles;

namespace GaltekClassroom.Agent.Service.SessionCommands;

public interface ISessionAgentServerVerifier
{
    bool Verify(NamedPipeClientStream pipe, int expectedSessionId);
}

public sealed class UnavailableSessionAgentServerVerifier : ISessionAgentServerVerifier
{
    public bool Verify(NamedPipeClientStream pipe, int expectedSessionId)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return false;
    }
}

public sealed class WindowsSessionAgentServerVerifier : ISessionAgentServerVerifier
{
    private readonly ISessionAgentProcessInspector _processInspector;
    private readonly string _expectedExecutablePath;

    public WindowsSessionAgentServerVerifier()
        : this(new WindowsSessionAgentProcessInspector(), DefaultExpectedExecutablePath())
    {
    }

    public WindowsSessionAgentServerVerifier(
        ISessionAgentProcessInspector processInspector,
        string expectedExecutablePath)
    {
        _processInspector = processInspector;
        _expectedExecutablePath = NormalizePath(expectedExecutablePath);
    }

    public bool Verify(NamedPipeClientStream pipe, int expectedSessionId)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (!OperatingSystem.IsWindows() || expectedSessionId <= 0)
        {
            return false;
        }

        if (!TryGetServerProcessId(pipe.SafePipeHandle, out var serverProcessId))
        {
            return false;
        }

        var process = _processInspector.TryGetProcess(serverProcessId);
        if (process is null)
        {
            return false;
        }

        return process.SessionId == expectedSessionId
            && string.Equals(
                NormalizePath(process.ExecutablePath),
                _expectedExecutablePath,
                StringComparison.OrdinalIgnoreCase);
    }

    public static string DefaultExpectedExecutablePath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return Path.Combine(
            programFiles,
            ProductInfo.DataDirectoryOrganizationName,
            ProductInfo.DataDirectoryProductName,
            "Agent",
            ProductInfo.SessionAgentInstallSubdirectory,
            ProductInfo.SessionAgentExecutableName);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }

    private static bool TryGetServerProcessId(SafePipeHandle pipeHandle, out int processId)
    {
        processId = 0;

        if (pipeHandle.IsInvalid || pipeHandle.IsClosed)
        {
            return false;
        }

        if (!GetNamedPipeServerProcessId(pipeHandle, out var serverProcessId))
        {
            return false;
        }

        if (serverProcessId == 0 || serverProcessId > int.MaxValue)
        {
            return false;
        }

        processId = (int)serverProcessId;
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe,
        out uint serverProcessId);
}

public interface ISessionAgentProcessInspector
{
    SessionAgentProcessInfo? TryGetProcess(int processId);
}

public sealed record SessionAgentProcessInfo(
    int ProcessId,
    int SessionId,
    string ExecutablePath);

public sealed class WindowsSessionAgentProcessInspector : ISessionAgentProcessInspector
{
    public SessionAgentProcessInfo? TryGetProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new SessionAgentProcessInfo(
                process.Id,
                process.SessionId,
                process.MainModule?.FileName ?? string.Empty);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or Win32Exception
            or NotSupportedException)
        {
            return null;
        }
    }
}
