using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Identity;

public sealed class MachineCodeGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IHostNameProvider _hostNameProvider;

    public MachineCodeGenerator(IHostNameProvider hostNameProvider)
    {
        _hostNameProvider = hostNameProvider;
    }

    public string Generate(InstallationIdentity identity)
    {
        return Generate(identity, _hostNameProvider.GetHostName());
    }

    public string Generate(InstallationIdentity identity, string hostname)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var payload = MachineCodePayload.FromIdentity(identity, hostname);
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
