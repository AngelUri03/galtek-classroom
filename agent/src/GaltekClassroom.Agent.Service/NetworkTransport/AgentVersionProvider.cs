using System.Reflection;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class AgentVersionProvider
{
    private readonly string _agentVersion = ResolveAgentVersion();

    public string GetAgentVersion()
    {
        return _agentVersion;
    }

    private static string ResolveAgentVersion()
    {
        var assembly = typeof(AgentVersionProvider).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
