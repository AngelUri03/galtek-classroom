using System.Runtime.Versioning;
using System.Security;
using System.Text.Json;
using GaltekClassroom.Agent.Shared;
using Microsoft.Win32;

namespace GaltekClassroom.Agent.Session.Commands;

public sealed record SessionApplicationResolverOptions(string DataDirectory);

public sealed record SessionApplicationResolution(
    bool Succeeded,
    string? ExecutablePath,
    string? ErrorCode,
    string? Message)
{
    public static SessionApplicationResolution Success(string executablePath)
    {
        return new SessionApplicationResolution(true, executablePath, null, null);
    }

    public static SessionApplicationResolution Failure(string errorCode, string message)
    {
        return new SessionApplicationResolution(false, null, errorCode, message);
    }
}

public interface ISessionApplicationResolver
{
    Task<SessionApplicationResolution> ResolveAsync(
        string applicationId,
        CancellationToken cancellationToken);
}

public sealed class UnavailableSessionApplicationResolver : ISessionApplicationResolver
{
    public Task<SessionApplicationResolution> ResolveAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SessionApplicationResolution.Failure(
            ApplicationBindingErrorCodes.ApplicationBindingNotFound,
            "Application binding catalog is unavailable."));
    }
}

public interface IWindowsAppPathsRegistry
{
    string? GetHklmDefaultExecutablePath(
        string executableName,
        WindowsAppPathsRegistryView view);
}

public enum WindowsAppPathsRegistryView
{
    Registry64,
    Registry32
}

public sealed class SessionApplicationResolver : ISessionApplicationResolver
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ApplicationLaunchTypeJsonConverter() }
    };

    private readonly string _catalogPath;
    private readonly IWindowsAppPathsRegistry _appPathsRegistry;

    public SessionApplicationResolver(
        SessionApplicationResolverOptions options,
        IWindowsAppPathsRegistry appPathsRegistry)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(appPathsRegistry);

        _catalogPath = Path.Combine(
            Path.GetFullPath(options.DataDirectory),
            ApplicationBindingConstants.FileName);
        _appPathsRegistry = appPathsRegistry;
    }

    public async Task<SessionApplicationResolution> ResolveAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        var idValidation = ApplicationBindingValidator.ValidateApplicationId(applicationId);
        if (!idValidation.IsValid)
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationBindingInvalid,
                "OPEN_APPLICATION request contains an invalid applicationId.");
        }

        if (!File.Exists(_catalogPath))
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationBindingNotFound,
                "Application binding was not found.");
        }

        ApplicationBindingCatalogDocument? document;
        try
        {
            await using var stream = new FileStream(
                _catalogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            document = await JsonSerializer.DeserializeAsync<ApplicationBindingCatalogDocument>(
                stream,
                ReadOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is JsonException
            or IOException
            or UnauthorizedAccessException)
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
                "Application binding catalog is invalid.");
        }

        var catalogValidation = ApplicationBindingValidator.ValidateCatalogDocument(document);
        if (!catalogValidation.IsValid || document is null)
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
                "Application binding catalog is invalid.");
        }

        var binding = document.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ApplicationId, idValidation.NormalizedValue, StringComparison.Ordinal));
        if (binding is null)
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationBindingNotFound,
                "Application binding was not found.");
        }

        if (!binding.Enabled)
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationDisabled,
                "Application binding is disabled.");
        }

        var bindingValidation = ApplicationBindingValidator.ValidateBinding(binding, requireAbsoluteExeExists: false);
        if (!bindingValidation.IsValid)
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
                "Application binding catalog is invalid.");
        }

        return binding.LaunchType switch
        {
            ApplicationLaunchType.AbsoluteExe => ResolveAbsoluteExe(binding.ExecutablePath),
            ApplicationLaunchType.AppPaths => ResolveAppPaths(binding.AppPathExecutableName),
            _ => Failure(
                ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
                "Application binding catalog is invalid.")
        };
    }

    private static SessionApplicationResolution ResolveAbsoluteExe(string? executablePath)
    {
        var validation = ApplicationBindingValidator.ValidateAbsoluteExePath(executablePath, requireExists: false);
        if (!validation.IsValid)
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
                "Application binding catalog is invalid.");
        }

        if (!File.Exists(validation.NormalizedValue))
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationExecutableNotFound,
                "Application executable was not found.");
        }

        return SessionApplicationResolution.Success(validation.NormalizedValue!);
    }

    private SessionApplicationResolution ResolveAppPaths(string? executableName)
    {
        var executableNameValidation = ApplicationBindingValidator.ValidateAppPathExecutableName(executableName);
        if (!executableNameValidation.IsValid)
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
                "Application binding catalog is invalid.");
        }

        var candidates = new List<string>(capacity: 2);
        foreach (var view in WindowsAppPathsRegistryViews.RegistryViewsForCurrentMachine())
        {
            var candidate = _appPathsRegistry.GetHklmDefaultExecutablePath(
                executableNameValidation.NormalizedValue!,
                view);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationExecutableNotFound,
                "Application executable was not found.");
        }

        string? resolved = null;
        foreach (var candidate in candidates)
        {
            var validation = ApplicationBindingValidator.ValidateAbsoluteExePath(candidate, requireExists: false);
            if (!validation.IsValid)
            {
                return Failure(
                    ApplicationBindingErrorCodes.ApplicationBindingInvalid,
                    "Application binding could not be resolved safely.");
            }

            if (resolved is null)
            {
                resolved = validation.NormalizedValue;
                continue;
            }

            if (!string.Equals(resolved, validation.NormalizedValue, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    ApplicationBindingErrorCodes.ApplicationBindingInvalid,
                    "Application binding has conflicting App Paths targets.");
            }
        }

        if (resolved is null || !File.Exists(resolved))
        {
            return Failure(
                ApplicationBindingErrorCodes.ApplicationExecutableNotFound,
                "Application executable was not found.");
        }

        return SessionApplicationResolution.Success(resolved);
    }

    private static SessionApplicationResolution Failure(string errorCode, string message)
    {
        return SessionApplicationResolution.Failure(errorCode, message);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAppPathsRegistry : IWindowsAppPathsRegistry
{
    private const string AppPathsSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";

    public string? GetHklmDefaultExecutablePath(
        string executableName,
        WindowsAppPathsRegistryView view)
    {
        var nameValidation = ApplicationBindingValidator.ValidateAppPathExecutableName(executableName);
        if (!nameValidation.IsValid)
        {
            return null;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, ToRegistryView(view));
            using var appKey = baseKey.OpenSubKey(AppPathsSubKey + nameValidation.NormalizedValue, writable: false);
            return appKey?.GetValue(null) as string;
        }
        catch (Exception exception) when (
            exception is IOException
            or SecurityException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static RegistryView ToRegistryView(WindowsAppPathsRegistryView view)
    {
        return view switch
        {
            WindowsAppPathsRegistryView.Registry64 => RegistryView.Registry64,
            WindowsAppPathsRegistryView.Registry32 => RegistryView.Registry32,
            _ => RegistryView.Registry32
        };
    }
}

public static class WindowsAppPathsRegistryViews
{
    public static IReadOnlyList<WindowsAppPathsRegistryView> RegistryViewsForCurrentMachine()
    {
        return Environment.Is64BitOperatingSystem
            ? [WindowsAppPathsRegistryView.Registry64, WindowsAppPathsRegistryView.Registry32]
            : [WindowsAppPathsRegistryView.Registry32];
    }
}
