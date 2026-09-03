using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Shared;

public static class ApplicationBindingConstants
{
    public const int SchemaVersion = 1;
    public const string FileName = "application-bindings.json";
}

public enum ApplicationLaunchType
{
    AppPaths,
    AbsoluteExe
}

public sealed record ApplicationBinding(
    string ApplicationId,
    ApplicationLaunchType LaunchType,
    string? AppPathExecutableName,
    string? ExecutablePath,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ApplicationBinding CreateAppPaths(
        string applicationId,
        string appPathExecutableName,
        DateTimeOffset nowUtc)
    {
        return new ApplicationBinding(
            applicationId,
            ApplicationLaunchType.AppPaths,
            appPathExecutableName,
            null,
            Enabled: true,
            nowUtc.ToUniversalTime(),
            nowUtc.ToUniversalTime());
    }

    public static ApplicationBinding CreateAbsoluteExe(
        string applicationId,
        string executablePath,
        DateTimeOffset nowUtc)
    {
        return new ApplicationBinding(
            applicationId,
            ApplicationLaunchType.AbsoluteExe,
            null,
            executablePath,
            Enabled: true,
            nowUtc.ToUniversalTime(),
            nowUtc.ToUniversalTime());
    }

    public ApplicationBinding WithUpdatedTarget(
        ApplicationLaunchType launchType,
        string? appPathExecutableName,
        string? executablePath,
        DateTimeOffset nowUtc)
    {
        return this with
        {
            LaunchType = launchType,
            AppPathExecutableName = appPathExecutableName,
            ExecutablePath = executablePath,
            Enabled = true,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public ApplicationBinding WithEnabled(bool enabled, DateTimeOffset nowUtc)
    {
        return this with
        {
            Enabled = enabled,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}

public sealed record ApplicationBindingCatalogDocument(
    int SchemaVersion,
    IReadOnlyList<ApplicationBinding> Bindings);

public static class ApplicationBindingErrorCodes
{
    public const string ApplicationBindingsInvalid = "APPLICATION_BINDINGS_INVALID";
    public const string ApplicationBindingNotFound = "APPLICATION_BINDING_NOT_FOUND";
    public const string ApplicationBindingAlreadyExists = "APPLICATION_BINDING_ALREADY_EXISTS";
    public const string ApplicationBindingInvalid = "APPLICATION_BINDING_INVALID";
    public const string ApplicationExecutableNotFound = "APPLICATION_EXECUTABLE_NOT_FOUND";
    public const string ApplicationDisabled = "APPLICATION_DISABLED";
    public const string ApplicationLaunchFailed = "APPLICATION_LAUNCH_FAILED";
    public const string AdministratorRequired = "ADMINISTRATOR_REQUIRED";
}

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
