using System.Runtime.InteropServices;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.SessionCommands;

public interface IInteractiveSessionResolver
{
    InteractiveSessionResolution Resolve();
}

public sealed record InteractiveSessionResolution(bool Available, int SessionId, string? ErrorCode)
{
    public static InteractiveSessionResolution Success(int sessionId)
    {
        return new InteractiveSessionResolution(true, sessionId, null);
    }

    public static InteractiveSessionResolution Unavailable(string errorCode)
    {
        return new InteractiveSessionResolution(false, 0, errorCode);
    }
}

public sealed class UnavailableInteractiveSessionResolver : IInteractiveSessionResolver
{
    public InteractiveSessionResolution Resolve()
    {
        return InteractiveSessionResolution.Unavailable(SessionCommandErrorCodes.SessionAgentUnavailable);
    }
}

public sealed class WindowsInteractiveSessionResolver : IInteractiveSessionResolver
{
    private const uint NoActiveSession = 0xFFFFFFFF;

    public InteractiveSessionResolution Resolve()
    {
        if (!OperatingSystem.IsWindows())
        {
            return InteractiveSessionResolution.Unavailable(SessionCommandErrorCodes.SessionAgentUnavailable);
        }

        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == NoActiveSession || sessionId == 0 || sessionId > int.MaxValue)
        {
            return InteractiveSessionResolution.Unavailable(SessionCommandErrorCodes.SessionAgentUnavailable);
        }

        return InteractiveSessionResolution.Success((int)sessionId);
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
