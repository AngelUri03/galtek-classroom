namespace GaltekClassroom.Agent.Service.Applications;

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

internal sealed record ApplicationBindingDocument(
    int SchemaVersion,
    IReadOnlyList<ApplicationBinding> Bindings);
