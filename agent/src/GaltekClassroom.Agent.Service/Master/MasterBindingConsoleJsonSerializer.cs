using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Service.Master;

public static class MasterBindingConsoleJsonSerializer
{
    private static readonly JsonSerializerOptions ConsoleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SerializeConfigurationResult(MasterBindingConfigurationResult result)
    {
        return JsonSerializer.Serialize(
            MasterBindingConfigurationConsoleDto.FromResult(result),
            ConsoleJsonOptions);
    }

    private sealed record MasterBindingConfigurationConsoleDto(
        string Status,
        bool Configured,
        string? AccountDisplayName,
        string? BlockingReason)
    {
        public static MasterBindingConfigurationConsoleDto FromResult(
            MasterBindingConfigurationResult result)
        {
            return new MasterBindingConfigurationConsoleDto(
                result.Status.ToCode(),
                result.Configured,
                result.AccountDisplayName,
                result.BlockingReason);
        }
    }
}
