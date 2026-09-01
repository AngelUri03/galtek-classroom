using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.Power;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class PowerOperationHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ShutdownOperation_RequestsPowerControllerWithRebootFalse()
    {
        var powerController = RecordingPowerController.Accepting();
        using var temp = new TempDirectory();
        var receiptStore = CreateReceiptStore(temp.Path);
        var dispatcher = CreateDispatcher(powerController, receiptStore: receiptStore);

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            CreateRequest("shutdown-1", NetworkOperationType.Shutdown),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.Unspecified, dispatch.Result.ErrorCode);
        Assert.Equal(new[] { false }, powerController.RebootRequests);
        Assert.NotNull(await receiptStore.TryGetSuccessResultAsync("shutdown-1", "device-1", CancellationToken.None));
    }

    [Fact]
    public async Task RestartOperation_RequestsPowerControllerWithRebootTrue()
    {
        var powerController = RecordingPowerController.Accepting();
        using var temp = new TempDirectory();
        var receiptStore = CreateReceiptStore(temp.Path);
        var dispatcher = CreateDispatcher(powerController, receiptStore: receiptStore);

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            CreateRequest("restart-1", NetworkOperationType.Restart),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.Unspecified, dispatch.Result.ErrorCode);
        Assert.Equal(new[] { true }, powerController.RebootRequests);
        Assert.NotNull(await receiptStore.TryGetSuccessResultAsync("restart-1", "device-1", CancellationToken.None));
    }

    [Fact]
    public async Task PowerOperation_SucceedsOnlyWhenWindowsAcceptsRequest()
    {
        var powerController = new RecordingPowerController(
            WindowsPowerControlResult.Failed(
                NetworkOperationErrorCode.PowerControlFailed,
                "Windows did not accept the power control request."));
        using var temp = new TempDirectory();
        var receiptStore = CreateReceiptStore(temp.Path);
        var dispatcher = CreateDispatcher(powerController, receiptStore: receiptStore);

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            CreateRequest("shutdown-not-accepted", NetworkOperationType.Shutdown),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, dispatch.Result.Status);
        Assert.NotEqual(OperationExecutionStatus.Success, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.PowerControlFailed, dispatch.Result.ErrorCode);
        Assert.Equal(new[] { false }, powerController.RebootRequests);
        Assert.Null(await receiptStore.TryGetSuccessResultAsync(
            "shutdown-not-accepted",
            "device-1",
            CancellationToken.None));
    }

    [Fact]
    public async Task PowerControllerFailure_ProducesStructuredFailedResult()
    {
        var powerController = new RecordingPowerController(
            WindowsPowerControlResult.Failed(
                NetworkOperationErrorCode.PowerControlUnavailable,
                "Power control is not supported on this platform."));
        var dispatcher = CreateDispatcher(powerController);

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            CreateRequest("restart-unavailable", NetworkOperationType.Restart),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.PowerControlUnavailable, dispatch.Result.ErrorCode);
        Assert.Equal("Power control is not supported on this platform.", dispatch.Result.Message);
        Assert.Equal(new[] { true }, powerController.RebootRequests);
    }

    [Fact]
    public async Task DuplicateOperationId_DoesNotRequestPowerControlTwice()
    {
        var powerController = RecordingPowerController.Accepting();
        var dispatcher = CreateDispatcher(powerController);
        OperationRequest request = CreateRequest("same-operation-id", NetworkOperationType.Shutdown);

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(request, CancellationToken.None);
        RemoteOperationDispatchResult second = await dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Equal(OperationExecutionStatus.Success, first.Result.Status);
        Assert.Equal(OperationExecutionStatus.Success, second.Result.Status);
        Assert.Equal(new[] { false }, powerController.RebootRequests);
    }

    [Fact]
    public async Task ReceiptStore_CanReadAcceptedPowerOperationAfterRecreation()
    {
        using var temp = new TempDirectory();
        var store = CreateReceiptStore(temp.Path);
        await store.SaveAcceptedAsync(
            CreateRequest("restart-durable", NetworkOperationType.Restart),
            FixedNow,
            CancellationToken.None);

        var recreated = CreateReceiptStore(temp.Path);
        OperationResult? result = await recreated.TryGetSuccessResultAsync(
            "restart-durable",
            "device-1",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(NetworkOperationType.Restart, result.OperationType);
    }

    [Fact]
    public async Task ReceiptStore_IsBoundedAndCleansLazilyWithoutHostedTimer()
    {
        using var temp = new TempDirectory();
        var clock = new MutableClock(FixedNow);
        var store = new PowerOperationReceiptStore(
            new PowerOperationReceiptStoreOptions(temp.Path, MaxReceipts: 2, Retention: TimeSpan.FromMinutes(5)),
            clock);

        await store.SaveAcceptedAsync(CreateRequest("old", NetworkOperationType.Shutdown), FixedNow, CancellationToken.None);
        clock.UtcNow = FixedNow.AddMinutes(1);
        await store.SaveAcceptedAsync(CreateRequest("kept-1", NetworkOperationType.Shutdown), clock.UtcNow, CancellationToken.None);
        clock.UtcNow = FixedNow.AddMinutes(2);
        await store.SaveAcceptedAsync(CreateRequest("kept-2", NetworkOperationType.Shutdown), clock.UtcNow, CancellationToken.None);

        Assert.Null(await store.TryGetSuccessResultAsync("old", "device-1", CancellationToken.None));
        Assert.NotNull(await store.TryGetSuccessResultAsync("kept-1", "device-1", CancellationToken.None));
        Assert.NotNull(await store.TryGetSuccessResultAsync("kept-2", "device-1", CancellationToken.None));
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(PowerOperationReceiptStore)));

        clock.UtcNow = FixedNow.AddMinutes(10);
        Assert.Null(await store.TryGetSuccessResultAsync("kept-1", "device-1", CancellationToken.None));
    }

    [Fact]
    public async Task DispatcherStatusLookup_ReturnsKnownFromCacheAndUnknownForMissingOrWrongTarget()
    {
        var handler = new CountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow));

        await dispatcher.DispatchAsync(CreateRequest("cached", NetworkOperationType.LockInput), CancellationToken.None);

        Assert.NotNull(dispatcher.TryGetCompletedResult("cached", "device-1"));
        Assert.Null(dispatcher.TryGetCompletedResult("cached", "other-device"));
        Assert.Null(dispatcher.TryGetCompletedResult("missing", "device-1"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DispatcherStatusLookup_DoesNotExecuteHandlerOnRepeatedQueries()
    {
        var handler = new CountingOperationHandler();
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow));

        await dispatcher.DispatchAsync(CreateRequest("cached-repeat", NetworkOperationType.LockInput), CancellationToken.None);

        _ = dispatcher.TryGetCompletedResult("cached-repeat", "device-1");
        _ = dispatcher.TryGetCompletedResult("cached-repeat", "device-1");

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CommercialLicenseNotActive_BlocksPowerHandler()
    {
        var powerController = RecordingPowerController.Accepting();
        var dispatcher = CreateDispatcher(
            powerController,
            new StaticLicenseStateProvider(LicenseState.Blocked(
                CommercialLicenseStatus.ActivationRequired,
                FixedNow,
                "Commercial license has not been resolved yet.")));

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            CreateRequest("shutdown-license-blocked", NetworkOperationType.Shutdown),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.OperationRejected, dispatch.Result.ErrorCode);
        Assert.Empty(powerController.RebootRequests);
    }

    [Fact]
    public void PowerHandlers_AreIdleUntilOperationRequestArrives()
    {
        var powerController = RecordingPowerController.Accepting();
        var shutdown = new ShutdownOperationHandler(
            powerController,
            NullLogger<ShutdownOperationHandler>.Instance);
        var restart = new RestartOperationHandler(
            powerController,
            NullLogger<RestartOperationHandler>.Instance);

        Assert.Equal(NetworkOperationType.Shutdown, shutdown.OperationType);
        Assert.Equal(NetworkOperationType.Restart, restart.OperationType);
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(ShutdownOperationHandler)));
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(RestartOperationHandler)));
        Assert.False(typeof(IHostedService).IsAssignableFrom(typeof(WindowsPowerController)));
        Assert.Empty(powerController.RebootRequests);
    }

    private static RemoteOperationDispatcher CreateDispatcher(
        IWindowsPowerController powerController,
        ILicenseStateProvider? licenseStateProvider = null,
        PowerOperationReceiptStore? receiptStore = null)
    {
        var clock = new MutableClock(FixedNow);
        return new RemoteOperationDispatcher(
            [
                new ShutdownOperationHandler(
                    powerController,
                    NullLogger<ShutdownOperationHandler>.Instance,
                    receiptStore,
                    clock),
                new RestartOperationHandler(
                    powerController,
                    NullLogger<RestartOperationHandler>.Instance,
                    receiptStore,
                    clock)
            ],
            new RemoteOperationOptions(),
            clock,
            licenseStateProvider);
    }

    private static PowerOperationReceiptStore CreateReceiptStore(string dataDirectory)
    {
        return new PowerOperationReceiptStore(
            new PowerOperationReceiptStoreOptions(dataDirectory),
            new MutableClock(FixedNow));
    }

    private static OperationRequest CreateRequest(
        string operationId,
        NetworkOperationType operationType)
    {
        return new OperationRequest
        {
            OperationId = operationId,
            OperationType = operationType,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds()
        };
    }

    private sealed class RecordingPowerController : IWindowsPowerController
    {
        private readonly Queue<WindowsPowerControlResult> _results;

        public RecordingPowerController(params WindowsPowerControlResult[] results)
        {
            IEnumerable<WindowsPowerControlResult> initialResults = results.Length == 0
                ? new[] { WindowsPowerControlResult.Success("Windows accepted the power control request.") }
                : results;
            _results = new Queue<WindowsPowerControlResult>(initialResults);
        }

        public List<bool> RebootRequests { get; } = [];

        public static RecordingPowerController Accepting()
        {
            return new RecordingPowerController(
                WindowsPowerControlResult.Success("Windows accepted the power control request."));
        }

        public Task<WindowsPowerControlResult> RequestShutdownAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RebootRequests.Add(false);
            return Task.FromResult(NextResult());
        }

        public Task<WindowsPowerControlResult> RequestRestartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RebootRequests.Add(true);
            return Task.FromResult(NextResult());
        }

        private WindowsPowerControlResult NextResult()
        {
            return _results.Count == 0
                ? WindowsPowerControlResult.Success("Windows accepted the power control request.")
                : _results.Dequeue();
        }
    }

    private sealed class CountingOperationHandler : IRemoteOperationHandler
    {
        public int Calls { get; private set; }

        public NetworkOperationType OperationType => NetworkOperationType.LockInput;

        public Task<RemoteOperationHandlerResult> HandleAsync(
            OperationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(RemoteOperationHandlerResult.Success("Handled."));
        }
    }

    private sealed class MutableClock : ISystemClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class StaticLicenseStateProvider : ILicenseStateProvider
    {
        public StaticLicenseStateProvider(LicenseState currentState)
        {
            CurrentState = currentState;
        }

        public LicenseState CurrentState { get; }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "GaltekClassroom.Agent.Power.Tests",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
