using System.Reflection;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class AgentVersionProvider
{
    public string GetAgentVersion()
    {
        var assembly = typeof(AgentVersionProvider).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
