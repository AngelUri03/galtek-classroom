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
    string Message,
    WindowsSessionStateResult? WindowsSessionState = null,
    ManagedAccountStatusResult? ManagedAccountStatus = null,
    WindowsAccountInventoryResult? WindowsAccountInventory = null)
{
    public static RemoteOperationHandlerResult Success(string message)
    {
        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Success,
            NetworkOperationErrorCode.Unspecified,
            message);
    }

    public static RemoteOperationHandlerResult Success(
        string message,
        WindowsSessionStateResult windowsSessionState)
    {
        ArgumentNullException.ThrowIfNull(windowsSessionState);

        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Success,
            NetworkOperationErrorCode.Unspecified,
            message,
            windowsSessionState);
    }

    public static RemoteOperationHandlerResult Success(
        string message,
        ManagedAccountStatusResult managedAccountStatus)
    {
        ArgumentNullException.ThrowIfNull(managedAccountStatus);

        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Success,
            NetworkOperationErrorCode.Unspecified,
            message,
            ManagedAccountStatus: managedAccountStatus);
    }

    public static RemoteOperationHandlerResult Success(
        string message,
        WindowsAccountInventoryResult windowsAccountInventory)
    {
        ArgumentNullException.ThrowIfNull(windowsAccountInventory);

        return new RemoteOperationHandlerResult(
            OperationExecutionStatus.Success,
            NetworkOperationErrorCode.Unspecified,
            message,
            WindowsAccountInventory: windowsAccountInventory);
    }

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
