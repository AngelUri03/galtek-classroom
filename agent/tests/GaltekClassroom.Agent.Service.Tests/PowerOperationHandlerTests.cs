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
        var dispatcher = CreateDispatcher(powerController);

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            CreateRequest("shutdown-1", NetworkOperationType.Shutdown),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.Unspecified, dispatch.Result.ErrorCode);
        Assert.Equal(new[] { false }, powerController.RebootRequests);
    }

    [Fact]
    public async Task RestartOperation_RequestsPowerControllerWithRebootTrue()
    {
        var powerController = RecordingPowerController.Accepting();
        var dispatcher = CreateDispatcher(powerController);

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            CreateRequest("restart-1", NetworkOperationType.Restart),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.Unspecified, dispatch.Result.ErrorCode);
        Assert.Equal(new[] { true }, powerController.RebootRequests);
    }

    [Fact]
    public async Task PowerOperation_SucceedsOnlyWhenWindowsAcceptsRequest()
    {
        var powerController = new RecordingPowerController(
            WindowsPowerControlResult.Failed(
                NetworkOperationErrorCode.PowerControlFailed,
                "Windows did not accept the power control request."));
        var dispatcher = CreateDispatcher(powerController);

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            CreateRequest("shutdown-not-accepted", NetworkOperationType.Shutdown),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, dispatch.Result.Status);
        Assert.NotEqual(OperationExecutionStatus.Success, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.PowerControlFailed, dispatch.Result.ErrorCode);
        Assert.Equal(new[] { false }, powerController.RebootRequests);
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
        ILicenseStateProvider? licenseStateProvider = null)
    {
        return new RemoteOperationDispatcher(
            [
                new ShutdownOperationHandler(
                    powerController,
                    NullLogger<ShutdownOperationHandler>.Instance),
                new RestartOperationHandler(
                    powerController,
                    NullLogger<RestartOperationHandler>.Instance)
            ],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow),
            licenseStateProvider);
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
}
