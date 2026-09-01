using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Protocol.Network.V1;
using GaltekClassroom.Agent.Service.Identity;

namespace GaltekClassroom.Agent.Service.Power;

public sealed class ShutdownOperationHandler : IRemoteOperationHandler
{
    private readonly IWindowsPowerController _powerController;
    private readonly ILogger<ShutdownOperationHandler> _logger;
    private readonly PowerOperationReceiptStore? _receiptStore;
    private readonly ISystemClock? _clock;

    public ShutdownOperationHandler(
        IWindowsPowerController powerController,
        ILogger<ShutdownOperationHandler> logger,
        PowerOperationReceiptStore? receiptStore = null,
        ISystemClock? clock = null)
    {
        _powerController = powerController;
        _logger = logger;
        _receiptStore = receiptStore;
        _clock = clock;
    }

    public NetworkOperationType OperationType => NetworkOperationType.Shutdown;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        return await HandlePowerRequestAsync(
            request,
            static (controller, token) => controller.RequestShutdownAsync(token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<RemoteOperationHandlerResult> HandlePowerRequestAsync(
        OperationRequest request,
        Func<IWindowsPowerController, CancellationToken, Task<WindowsPowerControlResult>> requestPowerControl,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Valid remote shutdown operation requested. OperationId: {OperationId}; TargetDeviceId: {TargetDeviceId}",
            request.OperationId,
            request.TargetDeviceId);

        try
        {
            WindowsPowerControlResult powerResult =
                await requestPowerControl(_powerController, cancellationToken).ConfigureAwait(false);

            if (powerResult.Accepted)
            {
                await TryPersistReceiptAsync(request, cancellationToken).ConfigureAwait(false);
                return RemoteOperationHandlerResult.Success(powerResult.Message);
            }

            _logger.LogWarning(
                "Remote shutdown operation failed. OperationId: {OperationId}; ErrorCode: {ErrorCode}",
                request.OperationId,
                powerResult.ErrorCode);

            return new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                powerResult.ErrorCode,
                powerResult.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Remote shutdown operation failed before Windows accepted it. OperationId: {OperationId}",
                request.OperationId);

            return new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                NetworkOperationErrorCode.PowerControlFailed,
                "Power control request failed.");
        }
    }

    private async Task TryPersistReceiptAsync(OperationRequest request, CancellationToken cancellationToken)
    {
        if (_receiptStore is null)
        {
            return;
        }

        try
        {
            await _receiptStore.SaveAcceptedAsync(
                request,
                _clock?.UtcNow ?? DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(
                exception,
                "Remote shutdown operation was accepted by Windows but durable receipt could not be persisted. OperationId: {OperationId}",
                request.OperationId);
        }
    }
}

public sealed class RestartOperationHandler : IRemoteOperationHandler
{
    private readonly IWindowsPowerController _powerController;
    private readonly ILogger<RestartOperationHandler> _logger;
    private readonly PowerOperationReceiptStore? _receiptStore;
    private readonly ISystemClock? _clock;

    public RestartOperationHandler(
        IWindowsPowerController powerController,
        ILogger<RestartOperationHandler> logger,
        PowerOperationReceiptStore? receiptStore = null,
        ISystemClock? clock = null)
    {
        _powerController = powerController;
        _logger = logger;
        _receiptStore = receiptStore;
        _clock = clock;
    }

    public NetworkOperationType OperationType => NetworkOperationType.Restart;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        return await HandlePowerRequestAsync(
            request,
            static (controller, token) => controller.RequestRestartAsync(token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<RemoteOperationHandlerResult> HandlePowerRequestAsync(
        OperationRequest request,
        Func<IWindowsPowerController, CancellationToken, Task<WindowsPowerControlResult>> requestPowerControl,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Valid remote restart operation requested. OperationId: {OperationId}; TargetDeviceId: {TargetDeviceId}",
            request.OperationId,
            request.TargetDeviceId);

        try
        {
            WindowsPowerControlResult powerResult =
                await requestPowerControl(_powerController, cancellationToken).ConfigureAwait(false);

            if (powerResult.Accepted)
            {
                await TryPersistReceiptAsync(request, cancellationToken).ConfigureAwait(false);
                return RemoteOperationHandlerResult.Success(powerResult.Message);
            }

            _logger.LogWarning(
                "Remote restart operation failed. OperationId: {OperationId}; ErrorCode: {ErrorCode}",
                request.OperationId,
                powerResult.ErrorCode);

            return new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                powerResult.ErrorCode,
                powerResult.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Remote restart operation failed before Windows accepted it. OperationId: {OperationId}",
                request.OperationId);

            return new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                NetworkOperationErrorCode.PowerControlFailed,
                "Power control request failed.");
        }
    }

    private async Task TryPersistReceiptAsync(OperationRequest request, CancellationToken cancellationToken)
    {
        if (_receiptStore is null)
        {
            return;
        }

        try
        {
            await _receiptStore.SaveAcceptedAsync(
                request,
                _clock?.UtcNow ?? DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(
                exception,
                "Remote restart operation was accepted by Windows but durable receipt could not be persisted. OperationId: {OperationId}",
                request.OperationId);
        }
    }
}
