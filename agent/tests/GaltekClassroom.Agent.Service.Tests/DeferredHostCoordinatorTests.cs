using System.Diagnostics;
using GaltekClassroom.Agent.Service.Diagnostics;
using GaltekClassroom.Agent.Service.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class DeferredHostCoordinatorTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.DeferredHost.Tests",
        Guid.NewGuid().ToString("N"));

    public DeferredHostCoordinatorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Start_ReturnsBelowFiveSeconds_WhenPostConnectInitializationTakesFortyFiveSeconds()
    {
        var factoryEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostics = Diagnostics();
        diagnostics.Write("PROCESS_ENTER");
        diagnostics.Write("SCM_CONNECT_BEGIN");
        diagnostics.Write("SCM_CONNECTED");
        await using var coordinator = new DeferredHostCoordinator(
            async cancellationToken =>
            {
                factoryEntered.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(45), cancellationToken);
                return new FakeHost();
            },
            diagnostics,
            _ => { });

        var stopwatch = Stopwatch.StartNew();
        coordinator.Start();
        stopwatch.Stop();
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _output.WriteLine("SCM_CALLBACK_RETURN_ELAPSED_MS={0:F3}", stopwatch.Elapsed.TotalMilliseconds);

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"The simulated SCM OnStart path took {stopwatch.Elapsed.TotalMilliseconds:F3} ms.");
        string contents = await ReadLogAsync();
        AssertMarkerOrder(contents, "SCM_CONNECTED", "HOST_INIT_BEGIN");
        Assert.DoesNotContain("HOST_INIT_COMPLETE", contents, StringComparison.Ordinal);

        await coordinator.StopAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RunAsync_RecordsConnectedInitializationAndReadinessInOrder()
    {
        var diagnostics = Diagnostics();
        var host = new FakeHost();
        diagnostics.Write("PROCESS_ENTER");
        diagnostics.Write("SCM_CONNECT_BEGIN");
        diagnostics.Write("SCM_CONNECTED");
        await using var coordinator = new DeferredHostCoordinator(
            _ => Task.FromResult<IHost>(host),
            diagnostics,
            _ => { });

        coordinator.Start();
        await host.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForMarkerAsync("AGENT_READY");
        string contents = await ReadLogAsync();

        AssertMarkerOrder(
            contents,
            "PROCESS_ENTER",
            "SCM_CONNECT_BEGIN",
            "SCM_CONNECTED",
            "HOST_INIT_BEGIN",
            "HOST_INIT_COMPLETE",
            "AGENT_START_BEGIN",
            "AGENT_READY");

        await coordinator.StopAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_DoesNotMarkAgentReadyUntilLocalReadinessIsSignaled()
    {
        var diagnostics = Diagnostics();
        var host = new FakeHost();
        var readiness = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new DeferredHostCoordinator(
            _ => Task.FromResult<IHost>(host),
            diagnostics,
            _ => { },
            async (_, cancellationToken) => await readiness.Task.WaitAsync(cancellationToken));

        coordinator.Start();
        await host.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain("AGENT_READY", await ReadLogAsync(), StringComparison.Ordinal);
        readiness.TrySetResult();
        await WaitForMarkerAsync("AGENT_READY");

        await coordinator.StopAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_WhenHostStopsBeforeReadiness_ReportsControlledFatalFailure()
    {
        var diagnostics = Diagnostics();
        var host = new FakeHost();
        Exception? reported = null;
        await using var coordinator = new DeferredHostCoordinator(
            _ => Task.FromResult<IHost>(host),
            diagnostics,
            exception => reported = exception,
            (_, cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));

        coordinator.Start();
        await host.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Lifetime.StopApplication();
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<InvalidOperationException>(reported);
        Assert.Contains("BOOTSTRAP_FATAL stage=AGENT_START", await ReadLogAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("AGENT_READY", await ReadLogAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenHostConstructionFails_ReportsSanitizedHostInitFailure()
    {
        var diagnostics = Diagnostics();
        Exception? reported = null;
        await using var coordinator = new DeferredHostCoordinator(
            _ => throw new InvalidOperationException("password=must-not-be-written"),
            diagnostics,
            exception => reported = exception);

        coordinator.Start();
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<InvalidOperationException>(reported);
        string contents = await ReadLogAsync();
        Assert.Contains("BOOTSTRAP_FATAL stage=HOST_INIT", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("must-not-be-written", contents, StringComparison.Ordinal);
    }

    private BootstrapStartupDiagnostics Diagnostics()
    {
        return new BootstrapStartupDiagnostics(LogPath());
    }

    private async Task WaitForMarkerAsync(string marker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if (File.Exists(LogPath())
                && (await ReadLogAsync(timeout.Token)).Contains(marker, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        throw new TimeoutException($"Marker '{marker}' was not written.");
    }

    private async Task<string> ReadLogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(
                LogPath(),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private string LogPath()
    {
        return Path.Combine(_directory, "startup-bootstrap.log");
    }

    private static void AssertMarkerOrder(string contents, params string[] markers)
    {
        var previousIndex = -1;
        foreach (string marker in markers)
        {
            int index = contents.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"Marker '{marker}' is absent or out of order.");
            previousIndex = index;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeHost : IHost
    {
        public FakeHost()
        {
            Lifetime = new FakeHostApplicationLifetime();
            Services = new ServiceCollection()
                .AddSingleton<IHostApplicationLifetime>(Lifetime)
                .BuildServiceProvider();
        }

        public FakeHostApplicationLifetime Lifetime { get; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IServiceProvider Services { get; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Lifetime.NotifyStarted();
            Started.TrySetResult();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Lifetime.StopApplication();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            (Services as IDisposable)?.Dispose();
            Lifetime.Dispose();
        }
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void NotifyStarted()
        {
            _started.Cancel();
        }

        public void StopApplication()
        {
            _stopping.Cancel();
            _stopped.Cancel();
        }

        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }
}
