using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using GaltekClassroom.Agent.Service.Diagnostics;
using GaltekClassroom.Agent.Service.Runtime;
using GaltekClassroom.Agent.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Hosting;

[SupportedOSPlatform("windows")]
public sealed class DeferredWindowsService : ServiceBase
{
    private const int ErrorExceptionInService = 1064;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(25);

    private readonly BootstrapStartupDiagnostics _diagnostics;
    private readonly DeferredHostCoordinator _coordinator;
    private int _stopping;

    public DeferredWindowsService(
        Func<CancellationToken, Task<IHost>> hostFactory,
        BootstrapStartupDiagnostics diagnostics)
    {
        ServiceName = ProductInfo.ServiceName;
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
        _diagnostics = diagnostics;
        _coordinator = new DeferredHostCoordinator(
            hostFactory,
            diagnostics,
            HandleFatalFailure,
            WaitForAgentReadinessAsync);
    }

    public static void RunService(
        Func<CancellationToken, Task<IHost>> hostFactory,
        BootstrapStartupDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(hostFactory);
        ArgumentNullException.ThrowIfNull(diagnostics);

        diagnostics.Write("SCM_CONNECT_BEGIN");
        using var service = new DeferredWindowsService(hostFactory, diagnostics);
        Run(service);
    }

    protected override void OnStart(string[] args)
    {
        // This is the first callback proving that ServiceBase.Run reached SCM.
        _diagnostics.Write("SCM_CONNECTED");
        _coordinator.Start();
    }

    protected override void OnStop()
    {
        StopCore();
    }

    protected override void OnShutdown()
    {
        StopCore();
        base.OnShutdown();
    }

    private void StopCore()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        try
        {
            RequestAdditionalTime((int)StopTimeout.TotalMilliseconds);
        }
        catch (InvalidOperationException)
        {
        }

        _coordinator.StopAsync(StopTimeout).GetAwaiter().GetResult();
    }

    private void HandleFatalFailure(Exception exception)
    {
        ReportCriticalFailure(exception);
        ExitCode = ErrorExceptionInService;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Stop();
            }
            catch
            {
                Environment.ExitCode = 1;
            }
        });
    }

    private static async Task WaitForAgentReadinessAsync(IHost host, CancellationToken cancellationToken)
    {
        var runtimeState = host.Services.GetRequiredService<AgentRuntimeState>();
        await runtimeState.WaitForInstallationIdentityAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ReportCriticalFailure(Exception exception)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (!EventLog.SourceExists(ProductInfo.ServiceName))
            {
                return;
            }

            string exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
            EventLog.WriteEntry(
                ProductInfo.ServiceName,
                $"Agent startup failed. exceptionType={exceptionType} hresult=0x{exception.HResult:X8}",
                EventLogEntryType.Error);
        }
        catch
        {
            // Critical reporting must never mask the original startup failure.
        }
    }
}
