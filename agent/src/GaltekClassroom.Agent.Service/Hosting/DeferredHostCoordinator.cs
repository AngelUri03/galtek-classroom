using GaltekClassroom.Agent.Service.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GaltekClassroom.Agent.Service.Hosting;

public sealed class DeferredHostCoordinator : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<IHost>> _hostFactory;
    private readonly BootstrapStartupDiagnostics _diagnostics;
    private readonly Action<Exception> _fatalFailure;
    private readonly Func<IHost, CancellationToken, Task> _readinessWaiter;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private Task? _runTask;

    public DeferredHostCoordinator(
        Func<CancellationToken, Task<IHost>> hostFactory,
        BootstrapStartupDiagnostics diagnostics,
        Action<Exception> fatalFailure,
        Func<IHost, CancellationToken, Task>? readinessWaiter = null)
    {
        _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _fatalFailure = fatalFailure ?? throw new ArgumentNullException(nameof(fatalFailure));
        _readinessWaiter = readinessWaiter ?? ((_, _) => Task.CompletedTask);
    }

    public Task Completion
    {
        get
        {
            lock (_gate)
            {
                return _runTask ?? Task.CompletedTask;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_runTask is not null)
            {
                return;
            }

            // Task.Run is intentional: ServiceBase.OnStart must return without
            // executing Host creation, DI construction, filesystem, crypto or network work.
            _runTask = Task.Run(RunAsync);
        }
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        _shutdown.Cancel();
        Task completion = Completion;
        if (completion.IsCompleted)
        {
            await ObserveAsync(completion).ConfigureAwait(false);
            return;
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        try
        {
            await completion.WaitAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            // SCM stop must remain bounded. Process teardown will release remaining resources.
        }
        catch
        {
            // Fatal startup is reported from RunAsync. Stop must remain idempotent.
        }
    }

    private async Task RunAsync()
    {
        IHost? host = null;
        var stage = BootstrapStartupStages.HostInit;
        try
        {
            _diagnostics.Write("HOST_INIT_BEGIN");
            host = await _hostFactory(_shutdown.Token).ConfigureAwait(false);
            _diagnostics.Write("HOST_INIT_COMPLETE");
            stage = BootstrapStartupStages.AgentStart;
            _diagnostics.Write("AGENT_START_BEGIN");
            await host.StartAsync(_shutdown.Token).ConfigureAwait(false);
            await WaitForReadinessAsync(host).ConfigureAwait(false);
            _diagnostics.Write("AGENT_READY");
            await WaitForStopSignalAsync(host, _shutdown.Token).ConfigureAwait(false);

            if (!_shutdown.IsCancellationRequested)
            {
                throw new InvalidOperationException("The Agent host stopped unexpectedly.");
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _diagnostics.WriteFatal(stage, exception);
            _fatalFailure(exception);
        }
        finally
        {
            if (host is not null)
            {
                try
                {
                    await host.StopAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                }
                catch
                {
                }

                host.Dispose();
            }
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task WaitForStopSignalAsync(IHost host, CancellationToken shutdownToken)
    {
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        using var stopSignal = CancellationTokenSource.CreateLinkedTokenSource(
            shutdownToken,
            lifetime.ApplicationStopping);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stopSignal.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopSignal.IsCancellationRequested)
        {
        }
    }

    private async Task WaitForReadinessAsync(IHost host)
    {
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        using var readinessSignal = CancellationTokenSource.CreateLinkedTokenSource(
            _shutdown.Token,
            lifetime.ApplicationStopping);

        try
        {
            await _readinessWaiter(host, readinessSignal.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            lifetime.ApplicationStopping.IsCancellationRequested
            && !_shutdown.IsCancellationRequested)
        {
            throw new InvalidOperationException("The Agent host stopped before becoming ready.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
