using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public interface IRemoteOperationHandler
{
    NetworkOperationType OperationType { get; }

    Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken);
}

public sealed record RemoteOperationHandlerResult(
    OperationExecutionStatus Status,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static RemoteOperationHandlerResult NotImplemented()
    {
        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Failed,
            NetworkOperationErrorCode.OperationNotImplemented,
            "Operation is not implemented by this Agent.");
    }

    public static RemoteOperationHandlerResult TimedOut()
    {
        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.TimedOut,
            NetworkOperationErrorCode.OperationTimeout,
            "Operation timed out.");
    }
}
