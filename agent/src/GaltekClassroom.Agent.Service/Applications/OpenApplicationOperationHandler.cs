using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.Applications;

public sealed class OpenApplicationOperationHandler : IRemoteOperationHandler
{
    private readonly IApplicationBindingStore _bindingStore;
    private readonly ISessionCommandClient _sessionCommandClient;
    private readonly ILogger<OpenApplicationOperationHandler> _logger;

    public OpenApplicationOperationHandler(
        IApplicationBindingStore bindingStore,
        ISessionCommandClient sessionCommandClient,
        ILogger<OpenApplicationOperationHandler> logger)
    {
        _bindingStore = bindingStore;
        _sessionCommandClient = sessionCommandClient;
        _logger = logger;
    }

    public NetworkOperationType OperationType => NetworkOperationType.OpenApplication;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        var applicationId = request.OpenApplication?.ApplicationId;
        var idValidation = ApplicationBindingValidator.ValidateApplicationId(applicationId);
        if (!idValidation.IsValid)
        {
            return Failed(
                NetworkOperationErrorCode.ApplicationBindingInvalid,
                "OPEN_APPLICATION request contains an invalid applicationId.");
        }

        var load = await _bindingStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return Failed(
                NetworkOperationErrorCode.ApplicationBindingsInvalid,
                "Application binding catalog is invalid.");
        }

        var binding = load.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ApplicationId, idValidation.NormalizedValue, StringComparison.Ordinal));
        if (binding is null)
        {
            return Failed(
                NetworkOperationErrorCode.ApplicationBindingNotFound,
                "Application binding was not found.");
        }

        if (!binding.Enabled)
        {
            return Failed(
                NetworkOperationErrorCode.ApplicationDisabled,
                "Application binding is disabled.");
        }

        var bindingValidation = ApplicationBindingValidator.ValidateBinding(binding, requireAbsoluteExeExists: false);
        if (!bindingValidation.IsValid)
        {
            return Failed(
                NetworkOperationErrorCode.ApplicationBindingsInvalid,
                "Application binding catalog is invalid.");
        }

        if (binding.LaunchType == ApplicationLaunchType.AbsoluteExe)
        {
            var pathValidation = ApplicationBindingValidator.ValidateAbsoluteExePath(
                binding.ExecutablePath,
                requireExists: true);
            if (pathValidation.Status == ApplicationBindingValidationStatus.ExecutableNotFound)
            {
                return Failed(
                    NetworkOperationErrorCode.ApplicationExecutableNotFound,
                    "Application executable was not found.");
            }

            if (!pathValidation.IsValid)
            {
                return Failed(
                    NetworkOperationErrorCode.ApplicationBindingsInvalid,
                    "Application binding catalog is invalid.");
            }
        }

        _logger.LogInformation(
            "Valid remote OPEN_APPLICATION operation requested. OperationId: {OperationId}; TargetDeviceId: {TargetDeviceId}; ApplicationId: {ApplicationId}",
            request.OperationId,
            request.TargetDeviceId,
            idValidation.NormalizedValue);

        var sessionResult = await _sessionCommandClient.OpenApplicationAsync(
            idValidation.NormalizedValue!,
            cancellationToken).ConfigureAwait(false);

        if (sessionResult.Succeeded)
        {
            return RemoteOperationHandlerResult.Success(
                "Windows accepted the request to open the application in the interactive session.");
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
            SessionCommandErrorCodes.SessionAgentUnavailable => NetworkOperationErrorCode.SessionAgentUnavailable,
            SessionCommandErrorCodes.SessionChannelUnauthorized => NetworkOperationErrorCode.SessionChannelUnauthorized,
            SessionCommandErrorCodes.SessionChannelProtocolMismatch => NetworkOperationErrorCode.SessionChannelProtocolMismatch,
            SessionCommandErrorCodes.SessionCommandNotSupported => NetworkOperationErrorCode.SessionChannelProtocolMismatch,
            SessionCommandErrorCodes.SessionChannelInvalidResponse => NetworkOperationErrorCode.SessionChannelInvalidResponse,
            SessionCommandErrorCodes.SessionChannelMalformedRequest => NetworkOperationErrorCode.SessionChannelInvalidResponse,
            SessionCommandErrorCodes.SessionCommandResultUnknown => NetworkOperationErrorCode.SessionCommandResultUnknown,
            SessionCommandErrorCodes.SessionChannelTimeout => NetworkOperationErrorCode.SessionAgentUnavailable,
            SessionCommandErrorCodes.ApplicationBindingsInvalid => NetworkOperationErrorCode.ApplicationBindingsInvalid,
            SessionCommandErrorCodes.ApplicationBindingNotFound => NetworkOperationErrorCode.ApplicationBindingNotFound,
            SessionCommandErrorCodes.ApplicationBindingInvalid => NetworkOperationErrorCode.ApplicationBindingInvalid,
            SessionCommandErrorCodes.ApplicationDisabled => NetworkOperationErrorCode.ApplicationDisabled,
            SessionCommandErrorCodes.ApplicationExecutableNotFound => NetworkOperationErrorCode.ApplicationExecutableNotFound,
            SessionCommandErrorCodes.ApplicationLaunchFailed => NetworkOperationErrorCode.ApplicationLaunchFailed,
            _ => NetworkOperationErrorCode.SessionChannelInvalidResponse
        };
    }

    private static string MessageFor(string? errorCode)
    {
        return errorCode switch
        {
            SessionCommandErrorCodes.SessionAgentUnavailable => "Session Agent is unavailable.",
            SessionCommandErrorCodes.SessionChannelUnauthorized => "Session command channel authorization failed.",
            SessionCommandErrorCodes.SessionChannelProtocolMismatch => "Session command protocol mismatch.",
            SessionCommandErrorCodes.SessionCommandNotSupported => "Session Agent does not support OPEN_APPLICATION.",
            SessionCommandErrorCodes.SessionChannelInvalidResponse => "Session Agent returned an invalid response.",
            SessionCommandErrorCodes.SessionChannelMalformedRequest => "Session Agent rejected the command request.",
            SessionCommandErrorCodes.SessionCommandResultUnknown => "Session command result is unknown after the OPEN_APPLICATION request was sent.",
            SessionCommandErrorCodes.SessionChannelTimeout => "Session Agent is unavailable.",
            SessionCommandErrorCodes.ApplicationBindingsInvalid => "Application binding catalog is invalid.",
            SessionCommandErrorCodes.ApplicationBindingNotFound => "Application binding was not found.",
            SessionCommandErrorCodes.ApplicationBindingInvalid => "Application binding is invalid.",
            SessionCommandErrorCodes.ApplicationDisabled => "Application binding is disabled.",
            SessionCommandErrorCodes.ApplicationExecutableNotFound => "Application executable was not found.",
            SessionCommandErrorCodes.ApplicationLaunchFailed => "Windows did not accept the application launch request.",
            _ => "Session command failed."
        };
    }
}
