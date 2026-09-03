using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Identity;

public static class AgentDataDirectory
{
    public const string EnvironmentVariableName = GaltekDataDirectory.EnvironmentVariableName;

    public static string Resolve(Func<string, string?>? getEnvironmentVariable = null)
    {
        return GaltekDataDirectory.Resolve(getEnvironmentVariable);
    }
}
