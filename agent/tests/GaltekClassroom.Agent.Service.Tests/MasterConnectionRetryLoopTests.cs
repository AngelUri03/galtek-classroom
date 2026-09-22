using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.NetworkTransport;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class MasterConnectionRetryLoopTests
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)
    ];

    [Fact]
    public async Task ImmediatelyAvailableMasterConnectsOnFirstAttempt()
    {
        using var stop = new CancellationTokenSource();
        var state = new MasterConnectionStateTracker();
        var attempts = 0;
        await CreateLoop(state, (_, _) => Task.CompletedTask).RunAsync(_ =>
        {
            attempts++;
            state.SetOnline(Guid.NewGuid(), DateTimeOffset.UtcNow);
            stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token);

        Assert.Equal(1, attempts);
        Assert.Equal(MasterConnectionState.Online, state.Snapshot.State);
    }

    [Fact]
    public async Task FailureAtStartupDoesNotEndLoopAndLaterConnects()
    {
        using var stop = new CancellationTokenSource();
        var state = new MasterConnectionStateTracker();
        var delays = new List<TimeSpan>();
        var attempts = 0;
        await CreateLoop(state, (delay, _) =>
        {
            delays.Add(delay);
            Assert.Equal(MasterConnectionState.Offline, state.Snapshot.State);
            return Task.CompletedTask;
        }).RunAsync(_ =>
        {
            attempts++;
            if (attempts == 1) throw new HttpRequestException("Master unavailable");
            state.SetOnline(Guid.NewGuid(), DateTimeOffset.UtcNow);
            stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token);

        Assert.Equal(2, attempts);
        Assert.Equal([Delays[0]], delays);
        Assert.Equal(MasterConnectionState.Online, state.Snapshot.State);
    }

    [Fact]
    public async Task SeveralFailuresUseBoundedBackoffThenRecoverWithoutProcessRestart()
    {
        using var stop = new CancellationTokenSource();
        var state = new MasterConnectionStateTracker();
        var delays = new List<TimeSpan>();
        var attempts = 0;
        await CreateLoop(state, (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        }).RunAsync(_ =>
        {
            attempts++;
            if (attempts <= 5) throw new IOException("offline");
            state.SetOnline(Guid.NewGuid(), DateTimeOffset.UtcNow);
            stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token);

        Assert.Equal(6, attempts);
        Assert.Equal([Delays[0], Delays[1], Delays[2], Delays[3], Delays[3]], delays);
    }

    [Fact]
    public async Task OnlineLossReturnsOfflineAndReconnectRunsHandshakeAgain()
    {
        using var stop = new CancellationTokenSource();
        var state = new MasterConnectionStateTracker();
        var delays = new List<TimeSpan>();
        var helloCount = 0;
        await CreateLoop(state, (delay, _) =>
        {
            delays.Add(delay);
            Assert.Equal(MasterConnectionState.Offline, state.Snapshot.State);
            return Task.CompletedTask;
        }).RunAsync(_ =>
        {
            helloCount++;
            state.SetOnline(Guid.NewGuid(), DateTimeOffset.UtcNow);
            if (helloCount == 1) throw new IOException("stream lost");
            stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token);

        Assert.Equal(2, helloCount);
        Assert.Equal([Delays[0]], delays);
        Assert.Equal(MasterConnectionState.Online, state.Snapshot.State);
    }

    [Fact]
    public async Task CancellationDuringBackoffExitsPromptly()
    {
        using var stop = new CancellationTokenSource();
        var state = new MasterConnectionStateTracker();
        var enteredDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loop = CreateLoop(state, async (_, token) =>
        {
            enteredDelay.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        var running = loop.RunAsync(_ => throw new IOException("offline"), stop.Token);
        await enteredDelay.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PersistentOutageWaitsAndDoesNotBusyLoop()
    {
        using var stop = new CancellationTokenSource();
        var state = new MasterConnectionStateTracker();
        var enteredDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var loop = CreateLoop(state, async (_, token) =>
        {
            enteredDelay.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        var running = loop.RunAsync(_ =>
        {
            attempts++;
            throw new HttpRequestException("offline");
        }, stop.Token);
        await enteredDelay.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, attempts);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task ReconnectDoesNotReplayPreviouslyProcessedOperation()
    {
        using var stop = new CancellationTokenSource();
        var state = new MasterConnectionStateTracker();
        var connectionAttempts = 0;
        var mutatingOperationCalls = 0;
        await CreateLoop(state, (_, _) => Task.CompletedTask).RunAsync(_ =>
        {
            connectionAttempts++;
            state.SetOnline(Guid.NewGuid(), DateTimeOffset.UtcNow);
            if (connectionAttempts == 1)
            {
                mutatingOperationCalls++;
                throw new IOException("result lost after execution");
            }

            stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token);

        Assert.Equal(2, connectionAttempts);
        Assert.Equal(1, mutatingOperationCalls);
    }

    private static MasterConnectionRetryLoop CreateLoop(
        MasterConnectionStateTracker state,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        return new MasterConnectionRetryLoop(
            new MasterConnectionOptions
            {
                Enabled = true,
                MasterNetworkIdentityId = Guid.NewGuid(),
                InitialConnectJitterMax = TimeSpan.Zero,
                ReconnectJitterMax = TimeSpan.Zero,
                ReconnectDelays = Delays
            },
            new ZeroJitter(),
            state,
            new FixedClock(),
            NullLogger.Instance,
            delay);
    }

    private sealed class ZeroJitter : IReconnectJitter
    {
        public TimeSpan NextJitter(TimeSpan maxJitter) => TimeSpan.Zero;
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
    }
}
