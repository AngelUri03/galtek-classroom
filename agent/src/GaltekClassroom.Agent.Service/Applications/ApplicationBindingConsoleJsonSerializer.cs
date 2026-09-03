using System.Text.Json;
using System.Text.Json.Serialization;

using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Applications;

public static class ApplicationBindingConsoleJsonSerializer
{
    private static readonly JsonSerializerOptions ConsoleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SerializeConfigurationResult(ApplicationBindingConfigurationResult result)
    {
        return JsonSerializer.Serialize(
            ApplicationBindingConfigurationConsoleDto.FromResult(result),
            ConsoleJsonOptions);
    }

    private sealed record ApplicationBindingConfigurationConsoleDto(
        string Status,
        bool Succeeded,
        IReadOnlyList<ApplicationBindingConsoleDto> Bindings,
        string? BlockingReason)
    {
        public static ApplicationBindingConfigurationConsoleDto FromResult(
            ApplicationBindingConfigurationResult result)
        {
            return new ApplicationBindingConfigurationConsoleDto(
                result.Status.ToCode(),
                result.Succeeded,
                result.Bindings.Select(ApplicationBindingConsoleDto.FromBinding).ToArray(),
                result.BlockingReason);
        }
    }

    private sealed record ApplicationBindingConsoleDto(
        string ApplicationId,
        string LaunchType,
        bool Enabled,
        string Target)
    {
        public static ApplicationBindingConsoleDto FromBinding(ApplicationBinding binding)
        {
            return new ApplicationBindingConsoleDto(
                binding.ApplicationId,
                FormatLaunchType(binding.LaunchType),
                binding.Enabled,
                binding.LaunchType == ApplicationLaunchType.AppPaths
                    ? binding.AppPathExecutableName ?? string.Empty
                    : binding.ExecutablePath ?? string.Empty);
        }

        private static string FormatLaunchType(ApplicationLaunchType launchType)
        {
            return launchType switch
            {
                ApplicationLaunchType.AppPaths => "APP_PATHS",
                ApplicationLaunchType.AbsoluteExe => "ABSOLUTE_EXE",
                _ => "UNKNOWN"
            };
        }
    }
}
