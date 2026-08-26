using System.Runtime.Versioning;
using System.Security.Principal;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Master;

public static class MasterBindingValidator
{
    public static bool IsValid(MasterWindowsBinding? binding, out string validationError)
    {
        validationError = string.Empty;

        if (binding is null)
        {
            validationError = "master-binding.json is empty.";
            return false;
        }

        if (binding.SchemaVersion != MasterBindingConstants.SchemaVersion)
        {
            validationError = "master-binding.json uses an unsupported schemaVersion.";
            return false;
        }

        if (binding.InstallationId == Guid.Empty)
        {
            validationError = "master-binding.json installationId is missing.";
            return false;
        }

        if (!IsValidSid(binding.WindowsSid))
        {
            validationError = "master-binding.json windowsSid is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(binding.AccountDisplayName))
        {
            validationError = "master-binding.json accountDisplayName is missing.";
            return false;
        }

        if (binding.BoundAtUtc == default)
        {
            validationError = "master-binding.json boundAtUtc is missing.";
            return false;
        }

        return true;
    }

    public static bool IsValidSid(string? windowsSid)
    {
        if (!LooksLikeSid(windowsSid))
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        return IsValidSidOnWindows(windowsSid!);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsValidSidOnWindows(string windowsSid)
    {
        try
        {
            var parsedSid = new SecurityIdentifier(windowsSid);
            return string.Equals(parsedSid.Value, windowsSid, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool LooksLikeSid(string? windowsSid)
    {
        if (string.IsNullOrWhiteSpace(windowsSid))
        {
            return false;
        }

        var parts = windowsSid.Split('-', StringSplitOptions.None);

        if (parts.Length < 4 || !string.Equals(parts[0], "S", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (parts[index].Length == 0 || !parts[index].All(char.IsDigit))
            {
                return false;
            }
        }

        return true;
    }
}
