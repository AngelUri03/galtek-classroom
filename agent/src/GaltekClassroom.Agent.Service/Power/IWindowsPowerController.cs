using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.Power;

public interface IWindowsPowerController
{
    Task<WindowsPowerControlResult> RequestShutdownAsync(CancellationToken cancellationToken);

    Task<WindowsPowerControlResult> RequestRestartAsync(CancellationToken cancellationToken);
}

public sealed record WindowsPowerControlResult(
    bool Accepted,
    NetworkOperationErrorCode ErrorCode,
    string Message)
{
    public static WindowsPowerControlResult Success(string message)
    {
        return new WindowsPowerControlResult(
            true,
            NetworkOperationErrorCode.Unspecified,
            message);
    }

    public static WindowsPowerControlResult Failed(
        NetworkOperationErrorCode errorCode,
        string message)
    {
        if (errorCode == NetworkOperationErrorCode.Unspecified)
        {
            throw new ArgumentException("Failed power control results require an error code.", nameof(errorCode));
        }

        return new WindowsPowerControlResult(false, errorCode, message);
    }
}
