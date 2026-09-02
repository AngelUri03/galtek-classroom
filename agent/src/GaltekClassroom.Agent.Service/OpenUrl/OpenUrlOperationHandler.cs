using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.OpenUrl;

public sealed class OpenUrlOperationHandler : IRemoteOperationHandler
{
    private readonly ISessionCommandClient _sessionCommandClient;
    private readonly OpenUrlSafetyPolicy _openUrlSafetyPolicy;
    private readonly ILogger<OpenUrlOperationHandler> _logger;

    public OpenUrlOperationHandler(
        ISessionCommandClient sessionCommandClient,
        ILogger<OpenUrlOperationHandler> logger)
        : this(sessionCommandClient, new OpenUrlSafetyPolicy(), logger)
    {
    }

    public OpenUrlOperationHandler(
        ISessionCommandClient sessionCommandClient,
        OpenUrlSafetyPolicy openUrlSafetyPolicy,
        ILogger<OpenUrlOperationHandler> logger)
    {
        _sessionCommandClient = sessionCommandClient;
        _openUrlSafetyPolicy = openUrlSafetyPolicy;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.OpenUrl;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        var url = request.OpenUrl?.Url;
        var validation = _openUrlSafetyPolicy.Validate(url);
        if (!validation.IsValid)
        {
            return Failed(
                NetworkOperationErrorCode.InvalidUrl,
                "OPEN_URL request contains an invalid URL.");
        }

        if (!Guid.TryParse(request.OperationId, out _))
        {
            return Failed(
                NetworkOperationErrorCode.ProtocolViolation,
                "OPEN_URL operationId is malformed.");
        }

        _logger.LogInformation(
            "Valid remote OPEN_URL operation requested. OperationId: {OperationId}; TargetDeviceId: {TargetDeviceId}",
            request.OperationId,
            request.TargetDeviceId);

        SessionCommandClientResult sessionResult =
            await _sessionCommandClient.OpenUrlAsync(
                request.OperationId,
                url!,
                cancellationToken).ConfigureAwait(false);

        if (sessionResult.Succeeded)
        {
            return RemoteOperationHandlerResult.Success(
                "Windows accepted the request to open the URL in the interactive session.");
        }

        return Failed(MapSessionError(sessionResult.ErrorCode), MessageFor(sessionResult.ErrorCode));
    }

    private static RemoteOperationHandlerResult Failed(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Failed,
            errorCode,
            message);
    }

    private static NetworkOperationErrorCode MapSessionError(string? errorCode)
    {
        return errorCode switch
        {
            SessionCommandErrorCodes.InvalidUrl => NetworkOperationErrorCode.InvalidUrl,
            SessionCommandErrorCodes.SessionAgentUnavailable => NetworkOperationErrorCode.SessionAgentUnavailable,
            SessionCommandErrorCodes.SessionChannelUnauthorized => NetworkOperationErrorCode.SessionChannelUnauthorized,
            SessionCommandErrorCodes.SessionChannelProtocolMismatch => NetworkOperationErrorCode.SessionChannelProtocolMismatch,
            SessionCommandErrorCodes.SessionCommandNotSupported => NetworkOperationErrorCode.SessionChannelProtocolMismatch,
            SessionCommandErrorCodes.SessionChannelInvalidResponse => NetworkOperationErrorCode.SessionChannelInvalidResponse,
            SessionCommandErrorCodes.SessionChannelMalformedRequest => NetworkOperationErrorCode.SessionChannelInvalidResponse,
            SessionCommandErrorCodes.SessionCommandResultUnknown => NetworkOperationErrorCode.SessionCommandResultUnknown,
            SessionCommandErrorCodes.SessionChannelTimeout => NetworkOperationErrorCode.SessionAgentUnavailable,
            SessionCommandErrorCodes.UrlLaunchFailed => NetworkOperationErrorCode.UrlLaunchFailed,
            _ => NetworkOperationErrorCode.SessionChannelInvalidResponse
        };
    }

    private static string MessageFor(string? errorCode)
    {
        return errorCode switch
        {
            SessionCommandErrorCodes.InvalidUrl => "OPEN_URL request contains an invalid URL.",
            SessionCommandErrorCodes.SessionAgentUnavailable => "Session Agent is unavailable.",
            SessionCommandErrorCodes.SessionChannelUnauthorized => "Session command channel authorization failed.",
            SessionCommandErrorCodes.SessionChannelProtocolMismatch => "Session command protocol mismatch.",
            SessionCommandErrorCodes.SessionCommandNotSupported => "Session Agent does not support OPEN_URL.",
            SessionCommandErrorCodes.SessionChannelInvalidResponse => "Session Agent returned an invalid response.",
            SessionCommandErrorCodes.SessionChannelMalformedRequest => "Session Agent rejected the command request.",
            SessionCommandErrorCodes.SessionCommandResultUnknown => "Session command result is unknown after the OPEN_URL request was sent.",
            SessionCommandErrorCodes.SessionChannelTimeout => "Session Agent is unavailable.",
            SessionCommandErrorCodes.UrlLaunchFailed => "Windows did not accept the URL launch request.",
            _ => "Session command failed."
        };
    }
}
