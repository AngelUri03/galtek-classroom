using System.Text.Json;

namespace GaltekClassroom.Agent.Service.Ipc;

internal static class LocalIpcJson
{
    public static JsonSerializerOptions CreateOptions(bool writeIndented = false)
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented
        };
    }
}
