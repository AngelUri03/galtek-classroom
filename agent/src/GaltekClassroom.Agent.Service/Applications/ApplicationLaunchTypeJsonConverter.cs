using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Service.Applications;

public sealed class ApplicationLaunchTypeJsonConverter : JsonConverter<ApplicationLaunchType>
{
    public override ApplicationLaunchType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();

        return value switch
        {
            "APP_PATHS" => ApplicationLaunchType.AppPaths,
            "ABSOLUTE_EXE" => ApplicationLaunchType.AbsoluteExe,
            _ => throw new JsonException("Unsupported application launch type.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ApplicationLaunchType value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ApplicationLaunchType.AppPaths => "APP_PATHS",
            ApplicationLaunchType.AbsoluteExe => "ABSOLUTE_EXE",
            _ => throw new JsonException("Unsupported application launch type.")
        });
    }
}
