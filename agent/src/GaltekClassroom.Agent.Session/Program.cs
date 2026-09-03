using System.Runtime.Loader;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Agent.Session;
using GaltekClassroom.Agent.Session.Commands;
using GaltekClassroom.Agent.Session.Ipc;
using GaltekClassroom.Agent.Session.Lifecycle;
using System.Runtime.InteropServices;
using System.Text.Json;

var commandLine = SessionAgentCommandLine.Parse(args);

if (!commandLine.IsValid)
{
    WindowsConsole.AttachToParentForCommandLine();
    await Console.Error.WriteLineAsync(commandLine.ErrorMessage);
    return 2;
}

if (commandLine.Mode == SessionAgentCommandMode.IpcStatus)
{
    WindowsConsole.AttachToParentForCommandLine();
    return await RunIpcStatusAsync(CancellationToken.None);
}

if (commandLine.Mode == SessionAgentCommandMode.IpcPing)
{
    WindowsConsole.AttachToParentForCommandLine();
    return await RunIpcPingAsync(CancellationToken.None);
}

if (commandLine.Mode == SessionAgentCommandMode.RuntimeDiagnostics)
{
    WindowsConsole.AttachToParentForCommandLine();
    Console.WriteLine(JsonSerializer.Serialize(
        RuntimeDiagnosticsSnapshot.CaptureCurrentProcess(ProductInfo.SessionAgentName),
        CreateConsoleJsonOptions()));
    return 0;
}

if (commandLine.Mode == SessionAgentCommandMode.AgentRuntimeDiagnostics)
{
    WindowsConsole.AttachToParentForCommandLine();
    return await RunAgentRuntimeDiagnosticsAsync(CancellationToken.None);
}

using var shutdown = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
EventHandler processExitHandler = (_, _) => shutdown.Cancel();
Action<AssemblyLoadContext> unloadHandler = _ => shutdown.Cancel();

Console.CancelKeyPress += cancelHandler;
AppDomain.CurrentDomain.ProcessExit += processExitHandler;
AssemblyLoadContext.Default.Unloading += unloadHandler;

try
{
    var supervisor = new SessionAgentSupervisor(
        new LocalAgentIpcClient(),
        new SystemSessionAgentDelay(),
        new SessionAgentSupervisorOptions());

    var backgroundHost = new SessionAgentBackgroundHost(
        new NamedMutexSessionInstanceLock(),
        new WindowsSessionContext(),
        supervisor,
        CreateSessionCommandServer());

    return await backgroundHost.RunAsync(shutdown.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
    AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
    AssemblyLoadContext.Default.Unloading -= unloadHandler;
}

static async Task<int> RunIpcStatusAsync(CancellationToken cancellationToken)
{
    try
    {
        var client = new LocalAgentIpcClient();
        var status = await client.GetDeviceStatusAsync(cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(status, CreateConsoleJsonOptions()));
        return 0;
    }
    catch (LocalAgentIpcException exception)
    {
        await Console.Error.WriteLineAsync($"{exception.ErrorCode}: {exception.Message}");
        return 1;
    }
}

static async Task<int> RunIpcPingAsync(CancellationToken cancellationToken)
{
    try
    {
        var client = new LocalAgentIpcClient();
        var ping = await client.PingAsync(cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(ping, CreateConsoleJsonOptions()));
        return 0;
    }
    catch (LocalAgentIpcException exception)
    {
        await Console.Error.WriteLineAsync($"{exception.ErrorCode}: {exception.Message}");
        return 1;
    }
}

static async Task<int> RunAgentRuntimeDiagnosticsAsync(CancellationToken cancellationToken)
{
    try
    {
        var client = new LocalAgentIpcClient();
        var diagnostics = await client.GetRuntimeDiagnosticsAsync(cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(diagnostics, CreateConsoleJsonOptions()));
        return 0;
    }
    catch (LocalAgentIpcException exception)
    {
        await Console.Error.WriteLineAsync($"{exception.ErrorCode}: {exception.Message}");
        return 1;
    }
}

static JsonSerializerOptions CreateConsoleJsonOptions()
{
    return new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

static ISessionCommandServer CreateSessionCommandServer()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        return new SessionCommandServer(
            new UnsupportedSessionCommandPipeStreamFactory(),
            new UnavailableSessionCommandCallerVerifier(),
            new UnavailableUrlLauncher(),
            new UnavailableSessionApplicationResolver(),
            new UnavailableWindowsApplicationLauncher());
    }

    return new SessionCommandServer(
        new SessionCommandPipeStreamFactory(),
        new WindowsSessionCommandCallerVerifier(),
        new WindowsUrlLauncher(),
        new SessionApplicationResolver(
            new SessionApplicationResolverOptions(GaltekDataDirectory.Resolve()),
            new WindowsAppPathsRegistry()),
        new WindowsApplicationLauncher());
}
