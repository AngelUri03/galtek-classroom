using GaltekClassroom.Agent.Session.Ipc;
using GaltekClassroom.Agent.Session.Lifecycle;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class SessionAgentSupervisorTests
{
    [Fact]
    public async Task RunAsync_WhenServiceIsUnavailable_DoesNotTerminate()
    {
        using var cancellation = new CancellationTokenSource();
        var delayObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ScriptedLocalAgentClient([false], status: CreateStatus());
        var delay = new RecordingDelay((_, _) => delayObserved.TrySetResult(), waitUntilCanceled: true);
        var supervisor = CreateSupervisor(client, delay);

        var runTask = supervisor.RunAsync(cancellation.Token);

        try
        {
            await delayObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(runTask.IsCompleted);
            Assert.Equal(SessionAgentLifecycleState.WaitingForService, supervisor.State);
        }
        finally
        {
            cancellation.Cancel();
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(SessionAgentLifecycleState.Stopping, supervisor.State);
    }

    [Fact]
    public async Task RunAsync_WhenServiceRecovers_ReachesReadyAndStoresDeviceStatus()
    {
        using var cancellation = new CancellationTokenSource();
        var readyObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedStatus = CreateStatus("ACTIVE");
        var client = new ScriptedLocalAgentClient([false, false, true], expectedStatus);
        var delay = new RecordingDelay();
        var supervisor = CreateSupervisor(client, delay);
        supervisor.StateChanged += state =>
        {
            if (state == SessionAgentLifecycleState.Ready)
            {
                readyObserved.TrySetResult();
                cancellation.Cancel();
            }
        };

        await supervisor.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(readyObserved.Task.IsCompleted);
        Assert.Equal(SessionAgentLifecycleState.Stopping, supervisor.State);
        Assert.Equal(expectedStatus, supervisor.LastDeviceStatus);
        Assert.Equal(3, client.PingCalls);
        Assert.Equal(1, client.DeviceStatusCalls);
    }

    [Fact]
    public async Task RunAsync_WhenServiceIsHealthy_PollsAfterHealthyInterval()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new ScriptedLocalAgentClient([true, true], CreateStatus());
        var delay = new RecordingDelay((delayValue, _) =>
        {
            if (delayValue == TimeSpan.FromSeconds(15))
            {
                cancellation.Cancel();
            }
        });
        var supervisor = CreateSupervisor(client, delay);

        await supervisor.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains(TimeSpan.FromSeconds(15), delay.RequestedDelays);
        Assert.DoesNotContain(TimeSpan.Zero, delay.RequestedDelays);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_StopsCleanly()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new ScriptedLocalAgentClient([false], CreateStatus());
        var delay = new RecordingDelay((_, _) => cancellation.Cancel());
        var supervisor = CreateSupervisor(client, delay);

        await supervisor.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SessionAgentLifecycleState.Stopping, supervisor.State);
    }

    private static SessionAgentSupervisor CreateSupervisor(
        ILocalAgentIpcClient client,
        ISessionAgentDelay delay)
    {
        return new SessionAgentSupervisor(
            client,
            delay,
            new SessionAgentSupervisorOptions());
    }

    private static LocalDeviceStatus CreateStatus(string licenseStatus = "ACTIVATION_REQUIRED")
    {
        return new LocalDeviceStatus
        {
            Product = ProductInfo.ProductCode,
            InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToString("D"),
            Hostname = "PC-AULA-07",
            LicenseStatus = licenseStatus,
            Active = string.Equals(licenseStatus, "ACTIVE", StringComparison.Ordinal)
        };
    }

    private sealed class RecordingDelay : ISessionAgentDelay
    {
        private readonly Action<TimeSpan, int>? _onDelay;
        private readonly bool _waitUntilCanceled;
        private readonly List<TimeSpan> _requestedDelays = [];

        public RecordingDelay(Action<TimeSpan, int>? onDelay = null, bool waitUntilCanceled = false)
        {
            _onDelay = onDelay;
            _waitUntilCanceled = waitUntilCanceled;
        }

        public IReadOnlyList<TimeSpan> RequestedDelays => _requestedDelays;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requestedDelays.Add(delay);
            _onDelay?.Invoke(delay, _requestedDelays.Count);
            cancellationToken.ThrowIfCancellationRequested();

            if (_waitUntilCanceled)
            {
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedLocalAgentClient : ILocalAgentIpcClient
    {
        private readonly Queue<bool> _pingResults;
        private readonly LocalDeviceStatus _status;

        public ScriptedLocalAgentClient(IEnumerable<bool> pingResults, LocalDeviceStatus status)
        {
            _pingResults = new Queue<bool>(pingResults);
            _status = status;
        }

        public int PingCalls { get; private set; }

        public int DeviceStatusCalls { get; private set; }

        public Task<LocalIpcPingPayload> PingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PingCalls++;

            var isAvailable = _pingResults.Count == 0 || _pingResults.Dequeue();
            if (!isAvailable)
            {
                throw new LocalAgentIpcException("LOCAL_AGENT_UNAVAILABLE", "Service is down.");
            }

            return Task.FromResult(new LocalIpcPingPayload());
        }

        public Task<LocalDeviceStatus> GetDeviceStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeviceStatusCalls++;
            return Task.FromResult(_status);
        }
    }
}
