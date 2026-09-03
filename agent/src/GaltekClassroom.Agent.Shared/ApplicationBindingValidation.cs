using System.Text.RegularExpressions;

namespace GaltekClassroom.Agent.Shared;

public enum ApplicationBindingValidationStatus
{
    Valid,
    Invalid,
    ExecutableNotFound
}

public sealed record ApplicationBindingValidationResult(
    ApplicationBindingValidationStatus Status,
    string? NormalizedValue,
    string? ErrorMessage)
{
    public bool IsValid => Status == ApplicationBindingValidationStatus.Valid;

    public static ApplicationBindingValidationResult Valid(string normalizedValue)
    {
        return new ApplicationBindingValidationResult(
            ApplicationBindingValidationStatus.Valid,
            normalizedValue,
            null);
    }

    public static ApplicationBindingValidationResult Invalid(string errorMessage)
    {
        return new ApplicationBindingValidationResult(
            ApplicationBindingValidationStatus.Invalid,
            null,
            errorMessage);
    }

    public static ApplicationBindingValidationResult ExecutableNotFound(string errorMessage)
    {
        return new ApplicationBindingValidationResult(
            ApplicationBindingValidationStatus.ExecutableNotFound,
            null,
            errorMessage);
    }
}

public static partial class ApplicationBindingValidator
{
    private const int MaxApplicationIdLength = 128;
    private const int MaxExecutableNameLength = 128;
    private const int MaxExecutablePathLength = 1024;

    public static ApplicationBindingValidationResult ValidateApplicationId(string? applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return ApplicationBindingValidationResult.Invalid("applicationId is required.");
        }

        if (!string.Equals(applicationId, applicationId.Trim(), StringComparison.Ordinal))
        {
            return ApplicationBindingValidationResult.Invalid("applicationId must not contain leading or trailing whitespace.");
        }

        if (applicationId.Length > MaxApplicationIdLength)
        {
            return ApplicationBindingValidationResult.Invalid("applicationId is too long.");
        }

        if (ContainsControlCharacter(applicationId))
        {
            return ApplicationBindingValidationResult.Invalid("applicationId must not contain control characters.");
        }

        if (!ApplicationIdRegex().IsMatch(applicationId))
        {
            return ApplicationBindingValidationResult.Invalid(
                "applicationId may contain only letters, numbers, dots, underscores and hyphens.");
        }

        return ApplicationBindingValidationResult.Valid(applicationId);
    }

    public static ApplicationBindingValidationResult ValidateAppPathExecutableName(string? executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return ApplicationBindingValidationResult.Invalid("appPathExecutableName is required.");
        }

        if (!string.Equals(executableName, executableName.Trim(), StringComparison.Ordinal))
        {
            return ApplicationBindingValidationResult.Invalid(
                "appPathExecutableName must not contain leading or trailing whitespace.");
        }

        if (executableName.Length > MaxExecutableNameLength)
        {
            return ApplicationBindingValidationResult.Invalid("appPathExecutableName is too long.");
        }

        if (ContainsControlCharacter(executableName)
            || executableName.Contains('"')
            || executableName.Any(char.IsWhiteSpace)
            || executableName.Contains('\\')
            || executableName.Contains('/')
            || executableName.Contains(':'))
        {
            return ApplicationBindingValidationResult.Invalid(
                "APP_PATHS requires a bare .exe file name without path, quotes or arguments.");
        }

        if (!string.Equals(Path.GetExtension(executableName), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationBindingValidationResult.Invalid("APP_PATHS executable name must end with .exe.");
        }

        if (!string.Equals(Path.GetFileName(executableName), executableName, StringComparison.Ordinal))
        {
            return ApplicationBindingValidationResult.Invalid("APP_PATHS executable name must not include a directory.");
        }

        return ApplicationBindingValidationResult.Valid(executableName);
    }

    public static ApplicationBindingValidationResult ValidateAbsoluteExePath(
        string? executablePath,
        bool requireExists)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return ApplicationBindingValidationResult.Invalid("executablePath is required.");
        }

        if (!string.Equals(executablePath, executablePath.Trim(), StringComparison.Ordinal))
        {
            return ApplicationBindingValidationResult.Invalid("executablePath must not contain leading or trailing whitespace.");
        }

        if (executablePath.Length > MaxExecutablePathLength)
        {
            return ApplicationBindingValidationResult.Invalid("executablePath is too long.");
        }

        if (ContainsControlCharacter(executablePath)
            || executablePath.Contains('"')
            || executablePath.Contains('*')
            || executablePath.Contains('?')
            || executablePath.Contains('%'))
        {
            return ApplicationBindingValidationResult.Invalid(
                "ABSOLUTE_EXE path must not contain control characters, quotes, wildcards or environment placeholders.");
        }

        if (executablePath.StartsWith(@"\\", StringComparison.Ordinal)
            || executablePath.StartsWith("//", StringComparison.Ordinal)
            || executablePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationBindingValidationResult.Invalid("ABSOLUTE_EXE path must be a local Windows path, not UNC or URI.");
        }

        if (!HasWindowsDriveRoot(executablePath))
        {
            return ApplicationBindingValidationResult.Invalid("ABSOLUTE_EXE path must be an absolute Windows drive path.");
        }

        if (ContainsParentSegment(executablePath))
        {
            return ApplicationBindingValidationResult.Invalid("ABSOLUTE_EXE path must not contain parent directory segments.");
        }

        if (executablePath.IndexOf(':', startIndex: 2) >= 0)
        {
            return ApplicationBindingValidationResult.Invalid("ABSOLUTE_EXE path must not contain alternate data streams.");
        }

        if (!string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationBindingValidationResult.Invalid("ABSOLUTE_EXE path must end with .exe.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(executablePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return ApplicationBindingValidationResult.Invalid("ABSOLUTE_EXE path is not valid.");
        }

        if (!HasWindowsDriveRoot(normalizedPath)
            || normalizedPath.StartsWith(@"\\", StringComparison.Ordinal)
            || normalizedPath.IndexOf(':', startIndex: 2) >= 0)
        {
            return ApplicationBindingValidationResult.Invalid("ABSOLUTE_EXE path must normalize to a local Windows .exe path.");
        }

        if (requireExists && !File.Exists(normalizedPath))
        {
            return ApplicationBindingValidationResult.ExecutableNotFound(
                "ABSOLUTE_EXE executable file does not exist.");
        }

        return ApplicationBindingValidationResult.Valid(normalizedPath);
    }

    public static ApplicationBindingValidationResult ValidateBinding(
        ApplicationBinding? binding,
        bool requireAbsoluteExeExists)
    {
        if (binding is null)
        {
            return ApplicationBindingValidationResult.Invalid("binding is required.");
        }

        var applicationId = ValidateApplicationId(binding.ApplicationId);
        if (!applicationId.IsValid)
        {
            return applicationId;
        }

        return binding.LaunchType switch
        {
            ApplicationLaunchType.AppPaths => ValidateAppPathsBinding(binding),
            ApplicationLaunchType.AbsoluteExe => ValidateAbsoluteExeBinding(binding, requireAbsoluteExeExists),
            _ => ApplicationBindingValidationResult.Invalid("binding has unsupported launchType.")
        };
    }

    public static ApplicationBindingValidationResult ValidateCatalogDocument(
        ApplicationBindingCatalogDocument? document)
    {
        if (document is null)
        {
            return ApplicationBindingValidationResult.Invalid("application-bindings.json is empty.");
        }

        if (document.SchemaVersion != ApplicationBindingConstants.SchemaVersion)
        {
            return ApplicationBindingValidationResult.Invalid("application-bindings.json has unsupported schemaVersion.");
        }

        if (document.Bindings is null)
        {
            return ApplicationBindingValidationResult.Invalid("application-bindings.json bindings are required.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in document.Bindings)
        {
            var validation = ValidateBinding(binding, requireAbsoluteExeExists: false);
            if (!validation.IsValid)
            {
                return validation;
            }

            if (!ids.Add(binding.ApplicationId))
            {
                return ApplicationBindingValidationResult.Invalid(
                    "application-bindings.json contains duplicate applicationId values.");
            }
        }

        return ApplicationBindingValidationResult.Valid(string.Empty);
    }

    private static ApplicationBindingValidationResult ValidateAppPathsBinding(ApplicationBinding binding)
    {
        if (binding.ExecutablePath is not null)
        {
            return ApplicationBindingValidationResult.Invalid("APP_PATHS binding must not include executablePath.");
        }

        return ValidateAppPathExecutableName(binding.AppPathExecutableName);
    }

    private static ApplicationBindingValidationResult ValidateAbsoluteExeBinding(
        ApplicationBinding binding,
        bool requireAbsoluteExeExists)
    {
        if (binding.AppPathExecutableName is not null)
        {
            return ApplicationBindingValidationResult.Invalid("ABSOLUTE_EXE binding must not include appPathExecutableName.");
        }

        return ValidateAbsoluteExePath(binding.ExecutablePath, requireAbsoluteExeExists);
    }

    private static bool HasWindowsDriveRoot(string path)
    {
        return path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/');
    }

    private static bool ContainsParentSegment(string path)
    {
        return path.Split(['\\', '/'], StringSplitOptions.None)
            .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static bool ContainsControlCharacter(string value)
    {
        return value.Any(char.IsControl);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationIdRegex();
}
