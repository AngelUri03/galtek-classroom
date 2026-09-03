using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.InputControl;

public sealed class LockInputOperationHandler : IRemoteOperationHandler
{
    private readonly ISessionCommandClient _sessionCommandClient;
    private readonly ILogger<LockInputOperationHandler> _logger;

    public LockInputOperationHandler(
        ISessionCommandClient sessionCommandClient,
        ILogger<LockInputOperationHandler> logger)
    {
        _sessionCommandClient = sessionCommandClient;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.LockInput;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.None)
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "LOCK_INPUT does not accept operation parameters.");
        }

        _logger.LogInformation(
            "Valid remote LOCK_INPUT operation requested. OperationId: {OperationId}; TargetDeviceId: {TargetDeviceId}",
            request.OperationId,
            request.TargetDeviceId);

        var sessionResult = await _sessionCommandClient.LockInputAsync(cancellationToken)
            .ConfigureAwait(false);
        return sessionResult.Succeeded
            ? RemoteOperationHandlerResult.Success("Windows accepted the input lock request.")
            : Failed(MapSessionError(sessionResult.ErrorCode), MessageFor(sessionResult.ErrorCode, lockOperation: true));
    }

    private static RemoteOperationHandlerResult Failed(NetworkOperationErrorCode errorCode, string message)
    {
        return new RemoteOperationHandlerResult(OperationExecutionStatus.Failed, errorCode, message);
    }

    internal static NetworkOperationErrorCode MapSessionError(string? errorCode)
    {
        return errorCode switch
        {
            SessionCommandErrorCodes.SessionAgentUnavailable => NetworkOperationErrorCode.SessionAgentUnavailable,
            SessionCommandErrorCodes.SessionChannelUnauthorized => NetworkOperationErrorCode.SessionChannelUnauthorized,
            SessionCommandErrorCodes.SessionChannelProtocolMismatch => NetworkOperationErrorCode.SessionChannelProtocolMismatch,
            SessionCommandErrorCodes.SessionCommandNotSupported => NetworkOperationErrorCode.SessionChannelProtocolMismatch,
            SessionCommandErrorCodes.SessionChannelInvalidResponse => NetworkOperationErrorCode.SessionChannelInvalidResponse,
            SessionCommandErrorCodes.SessionChannelMalformedRequest => NetworkOperationErrorCode.SessionChannelInvalidResponse,
            SessionCommandErrorCodes.SessionCommandResultUnknown => NetworkOperationErrorCode.SessionCommandResultUnknown,
            SessionCommandErrorCodes.SessionChannelTimeout => NetworkOperationErrorCode.SessionAgentUnavailable,
            SessionCommandErrorCodes.InputLockFailed => NetworkOperationErrorCode.InputLockFailed,
            SessionCommandErrorCodes.InputUnlockFailed => NetworkOperationErrorCode.InputUnlockFailed,
            _ => NetworkOperationErrorCode.SessionChannelInvalidResponse
        };
    }

    internal static string MessageFor(string? errorCode, bool lockOperation)
    {
        return errorCode switch
        {
            SessionCommandErrorCodes.SessionAgentUnavailable => "Session Agent is unavailable.",
            SessionCommandErrorCodes.SessionChannelUnauthorized => "Session command channel authorization failed.",
            SessionCommandErrorCodes.SessionChannelProtocolMismatch => "Session command protocol mismatch.",
            SessionCommandErrorCodes.SessionCommandNotSupported => lockOperation
                ? "Session Agent does not support LOCK_INPUT."
                : "Session Agent does not support UNLOCK_INPUT.",
            SessionCommandErrorCodes.SessionChannelInvalidResponse => "Session Agent returned an invalid response.",
            SessionCommandErrorCodes.SessionChannelMalformedRequest => "Session Agent rejected the command request.",
            SessionCommandErrorCodes.SessionCommandResultUnknown => lockOperation
                ? "Session command result is unknown after the LOCK_INPUT request was sent."
                : "Session command result is unknown after the UNLOCK_INPUT request was sent.",
            SessionCommandErrorCodes.SessionChannelTimeout => "Session Agent is unavailable.",
            SessionCommandErrorCodes.InputLockFailed => "Windows did not confirm input lock.",
            SessionCommandErrorCodes.InputUnlockFailed => "Windows did not confirm input unlock.",
            _ => "Session command failed."
        };
    }
}

public sealed class UnlockInputOperationHandler : IRemoteOperationHandler
{
    private readonly ISessionCommandClient _sessionCommandClient;
    private readonly ILogger<UnlockInputOperationHandler> _logger;

    public UnlockInputOperationHandler(
        ISessionCommandClient sessionCommandClient,
        ILogger<UnlockInputOperationHandler> logger)
    {
        _sessionCommandClient = sessionCommandClient;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.UnlockInput;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.None)
        {
            return new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                NetworkOperationErrorCode.ProtocolViolation,
                "UNLOCK_INPUT does not accept operation parameters.");
        }

        _logger.LogInformation(
            "Valid remote UNLOCK_INPUT operation requested. OperationId: {OperationId}; TargetDeviceId: {TargetDeviceId}",
            request.OperationId,
            request.TargetDeviceId);

        var sessionResult = await _sessionCommandClient.UnlockInputAsync(cancellationToken)
            .ConfigureAwait(false);
        return sessionResult.Succeeded
            ? RemoteOperationHandlerResult.Success("Windows accepted the input unlock request.")
            : new RemoteOperationHandlerResult(
                OperationExecutionStatus.Failed,
                LockInputOperationHandler.MapSessionError(sessionResult.ErrorCode),
                LockInputOperationHandler.MessageFor(sessionResult.ErrorCode, lockOperation: false));
    }
}
