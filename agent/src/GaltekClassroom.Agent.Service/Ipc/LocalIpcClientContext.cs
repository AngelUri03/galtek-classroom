namespace GaltekClassroom.Agent.Service.Ipc;

public sealed record LocalIpcClientContext(
    string? WindowsSid,
    string? AccountName)
{
    public static LocalIpcClientContext Unavailable()
    {
        return new LocalIpcClientContext(null, null);
    }
}
