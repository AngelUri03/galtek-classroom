using GaltekClassroom.Agent.Session.Commands;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class InputBlockCoordinatorTests
{
    [Fact]
    public void IdleBeforeFirstLock_DoesNotCallNativeApi()
    {
        var api = new RecordingInputBlockApi();
        _ = new WindowsInputBlockCoordinator(api);

        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task Lock_CreatesDedicatedWorkerAndCallsBlockInputTrue()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var api = new RecordingInputBlockApi(true);
        var coordinator = new WindowsInputBlockCoordinator(api);

        var result = await coordinator.LockAsync(CancellationToken.None);
        await coordinator.CleanupAsync(TimeSpan.FromSeconds(1));

        Assert.True(result.Succeeded);
        Assert.Equal([true, false], api.Calls.Select(call => call.Block).ToArray());
        var call = api.Calls[0];
        Assert.True(call.Block);
        Assert.NotEqual(callerThreadId, call.ManagedThreadId);
    }

    [Fact]
    public async Task Unlock_AfterLock_CallsBlockInputFalseOnSameThread()
    {
        var api = new RecordingInputBlockApi(true, true);
        var coordinator = new WindowsInputBlockCoordinator(api);

        await coordinator.LockAsync(CancellationToken.None);
        var unlock = await coordinator.UnlockAsync(CancellationToken.None);

        Assert.True(unlock.Succeeded);
        Assert.Equal([true, false], api.Calls.Select(call => call.Block).ToArray());
        Assert.Equal(api.Calls[0].ManagedThreadId, api.Calls[1].ManagedThreadId);
    }

    [Fact]
    public async Task RepeatedLock_UsesExistingOwnerThread()
    {
        var api = new RecordingInputBlockApi(true, true, true);
        var coordinator = new WindowsInputBlockCoordinator(api);

        await coordinator.LockAsync(CancellationToken.None);
        var repeated = await coordinator.LockAsync(CancellationToken.None);
        await coordinator.UnlockAsync(CancellationToken.None);

        Assert.True(repeated.Succeeded);
        Assert.Equal([true, true, false], api.Calls.Select(call => call.Block).ToArray());
        Assert.Equal(api.Calls[0].ManagedThreadId, api.Calls[1].ManagedThreadId);
        Assert.Equal(api.Calls[0].ManagedThreadId, api.Calls[2].ManagedThreadId);
    }

    [Fact]
    public async Task RepeatedLock_WhenNativeReportsAlreadyBlocked_IsIdempotentSuccess()
    {
        var api = new RecordingInputBlockApi(true, false, true);
        var coordinator = new WindowsInputBlockCoordinator(api);

        await coordinator.LockAsync(CancellationToken.None);
        var repeated = await coordinator.LockAsync(CancellationToken.None);
        await coordinator.UnlockAsync(CancellationToken.None);

        Assert.True(repeated.Succeeded);
        Assert.Equal([true, true, false], api.Calls.Select(call => call.Block).ToArray());
    }

    [Fact]
    public async Task FirstLockFailure_DoesNotLeaveLockedState()
    {
        var api = new RecordingInputBlockApi(false);
        var coordinator = new WindowsInputBlockCoordinator(api);

        var lockResult = await coordinator.LockAsync(CancellationToken.None);
        var unlockResult = await coordinator.UnlockAsync(CancellationToken.None);

        Assert.False(lockResult.Succeeded);
        Assert.Equal(SessionCommandErrorCodes.InputLockFailed, lockResult.ErrorCode);
        Assert.True(unlockResult.Succeeded);
        Assert.Equal([true], api.Calls.Select(call => call.Block).ToArray());
    }

    [Fact]
    public async Task UnlockWithoutActiveLock_IsSuccessAndDoesNotCallNativeApi()
    {
        var api = new RecordingInputBlockApi();
        var coordinator = new WindowsInputBlockCoordinator(api);

        var result = await coordinator.UnlockAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task UnlockFailure_ReturnsFailureAndAllowsNewLock()
    {
        var api = new RecordingInputBlockApi(true, false, true);
        var coordinator = new WindowsInputBlockCoordinator(api);

        await coordinator.LockAsync(CancellationToken.None);
        var unlock = await coordinator.UnlockAsync(CancellationToken.None);
        var nextLock = await coordinator.LockAsync(CancellationToken.None);
        await coordinator.CleanupAsync(TimeSpan.FromSeconds(1));

        Assert.False(unlock.Succeeded);
        Assert.Equal(SessionCommandErrorCodes.InputUnlockFailed, unlock.ErrorCode);
        Assert.True(nextLock.Succeeded);
        Assert.Equal([true, false, true, false], api.Calls.Select(call => call.Block).ToArray());
    }

    [Fact]
    public async Task CleanupWithActiveLock_UnlocksFromOwnerThread()
    {
        var api = new RecordingInputBlockApi(true, true);
        var coordinator = new WindowsInputBlockCoordinator(api);

        await coordinator.LockAsync(CancellationToken.None);
        var cleanup = await coordinator.CleanupAsync(TimeSpan.FromSeconds(1));

        Assert.True(cleanup.Succeeded);
        Assert.Equal([true, false], api.Calls.Select(call => call.Block).ToArray());
        Assert.Equal(api.Calls[0].ManagedThreadId, api.Calls[1].ManagedThreadId);
    }

    [Fact]
    public async Task CleanupWithoutActiveLock_DoesNotCreateWorker()
    {
        var api = new RecordingInputBlockApi();
        var coordinator = new WindowsInputBlockCoordinator(api);

        var cleanup = await coordinator.CleanupAsync(TimeSpan.FromSeconds(1));

        Assert.True(cleanup.Succeeded);
        Assert.Empty(api.Calls);
    }

    private sealed class RecordingInputBlockApi : IWindowsInputBlockApi
    {
        private readonly Queue<bool> _results;
        private readonly object _sync = new();

        public RecordingInputBlockApi(params bool[] results)
        {
            _results = new Queue<bool>(results);
        }

        public List<InputBlockCall> Calls { get; } = [];

        public bool BlockInput(bool block)
        {
            lock (_sync)
            {
                Calls.Add(new InputBlockCall(block, Environment.CurrentManagedThreadId));
                return _results.Count == 0 || _results.Dequeue();
            }
        }
    }

    private sealed record InputBlockCall(bool Block, int ManagedThreadId);
}
