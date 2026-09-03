namespace GaltekClassroom.Agent.Shared;

public static class GaltekDataDirectory
{
    public const string EnvironmentVariableName = "GALTEK_CLASSROOM_DATA_DIR";

    public static string Resolve(Func<string, string?>? getEnvironmentVariable = null)
    {
        var environment = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var configuredDirectory = environment(EnvironmentVariableName);

        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return Path.Combine(
            commonApplicationData,
            ProductInfo.DataDirectoryOrganizationName,
            ProductInfo.DataDirectoryProductName);
    }
}
