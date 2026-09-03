using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Persistence;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Applications;

public sealed record ApplicationBindingStoreOptions(string DataDirectory);

public enum ApplicationBindingStoreReadStatus
{
    Loaded,
    Invalid
}

public sealed record ApplicationBindingStoreReadResult(
    ApplicationBindingStoreReadStatus Status,
    IReadOnlyList<ApplicationBinding> Bindings,
    string FilePath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Loaded => Status == ApplicationBindingStoreReadStatus.Loaded;

    public static ApplicationBindingStoreReadResult LoadedBindings(
        IReadOnlyList<ApplicationBinding> bindings,
        string filePath)
    {
        return new ApplicationBindingStoreReadResult(
            ApplicationBindingStoreReadStatus.Loaded,
            bindings,
            filePath,
            null,
            null);
    }

    public static ApplicationBindingStoreReadResult Invalid(
        string filePath,
        string errorMessage)
    {
        return new ApplicationBindingStoreReadResult(
            ApplicationBindingStoreReadStatus.Invalid,
            Array.Empty<ApplicationBinding>(),
            filePath,
            ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
            errorMessage);
    }
}

public enum ApplicationBindingStoreWriteStatus
{
    Configured,
    Removed,
    AlreadyExists,
    NotFound,
    Invalid,
    ExecutableNotFound,
    StoreInvalid,
    VerificationFailed
}

public sealed record ApplicationBindingStoreWriteResult(
    ApplicationBindingStoreWriteStatus Status,
    ApplicationBinding? Binding,
    string FilePath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Succeeded => Status is ApplicationBindingStoreWriteStatus.Configured
        or ApplicationBindingStoreWriteStatus.Removed;

    public static ApplicationBindingStoreWriteResult Success(
        ApplicationBindingStoreWriteStatus status,
        ApplicationBinding? binding,
        string filePath)
    {
        return new ApplicationBindingStoreWriteResult(status, binding, filePath, null, null);
    }

    public static ApplicationBindingStoreWriteResult Failure(
        ApplicationBindingStoreWriteStatus status,
        string filePath,
        string errorCode,
        string errorMessage)
    {
        return new ApplicationBindingStoreWriteResult(status, null, filePath, errorCode, errorMessage);
    }
}

public interface IApplicationBindingStore
{
    string FilePath { get; }

    Task<ApplicationBindingStoreReadResult> LoadAsync(CancellationToken cancellationToken);

    Task<ApplicationBindingStoreReadResult> ListAsync(CancellationToken cancellationToken);

    Task<ApplicationBindingStoreReadResult> GetAsync(
        string applicationId,
        CancellationToken cancellationToken);

    Task<ApplicationBindingStoreWriteResult> AddAppPathAsync(
        string applicationId,
        string appPathExecutableName,
        bool replaceExisting,
        CancellationToken cancellationToken);

    Task<ApplicationBindingStoreWriteResult> AddAbsoluteExeAsync(
        string applicationId,
        string executablePath,
        bool replaceExisting,
        CancellationToken cancellationToken);

    Task<ApplicationBindingStoreWriteResult> ReplaceAsync(
        ApplicationBinding binding,
        CancellationToken cancellationToken);

    Task<ApplicationBindingStoreWriteResult> SetEnabledAsync(
        string applicationId,
        bool enabled,
        CancellationToken cancellationToken);

    Task<ApplicationBindingStoreWriteResult> RemoveAsync(
        string applicationId,
        CancellationToken cancellationToken);
}

public sealed class ApplicationBindingStore : IApplicationBindingStore
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ApplicationLaunchTypeJsonConverter() }
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new ApplicationLaunchTypeJsonConverter() }
    };

    private readonly string _dataDirectory;
    private readonly string _filePath;
    private readonly IApplicationBindingFileSecurity _fileSecurity;
    private readonly ISystemClock _clock;

    public ApplicationBindingStore(
        ApplicationBindingStoreOptions options,
        IApplicationBindingFileSecurity fileSecurity,
        ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSecurity);
        ArgumentNullException.ThrowIfNull(clock);

        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, ApplicationBindingConstants.FileName);
        _fileSecurity = fileSecurity;
        _clock = clock;
    }

    public string FilePath => _filePath;

    public Task<ApplicationBindingStoreReadResult> ListAsync(CancellationToken cancellationToken)
    {
        return LoadAsync(cancellationToken);
    }

    public async Task<ApplicationBindingStoreReadResult> GetAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        var idValidation = ApplicationBindingValidator.ValidateApplicationId(applicationId);
        if (!idValidation.IsValid)
        {
            return ApplicationBindingStoreReadResult.Invalid(
                _filePath,
                idValidation.ErrorMessage ?? "applicationId is invalid.");
        }

        var load = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return load;
        }

        var binding = Find(load.Bindings, idValidation.NormalizedValue!);

        return ApplicationBindingStoreReadResult.LoadedBindings(
            binding is null ? Array.Empty<ApplicationBinding>() : [binding],
            _filePath);
    }

    public async Task<ApplicationBindingStoreReadResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return ApplicationBindingStoreReadResult.LoadedBindings(Array.Empty<ApplicationBinding>(), _filePath);
        }

        try
        {
            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            var document = await JsonSerializer.DeserializeAsync<ApplicationBindingCatalogDocument>(
                stream,
                ReadOptions,
                cancellationToken).ConfigureAwait(false);

            var validation = ApplicationBindingValidator.ValidateCatalogDocument(document);
            if (!validation.IsValid)
            {
                return ApplicationBindingStoreReadResult.Invalid(
                    _filePath,
                    validation.ErrorMessage ?? "application-bindings.json is invalid.");
            }

            return ApplicationBindingStoreReadResult.LoadedBindings(
                document!.Bindings.ToArray(),
                _filePath);
        }
        catch (JsonException exception)
        {
            return ApplicationBindingStoreReadResult.Invalid(
                _filePath,
                $"application-bindings.json is corrupt or incomplete: {exception.Message}");
        }
        catch (IOException exception)
        {
            return ApplicationBindingStoreReadResult.Invalid(
                _filePath,
                $"application-bindings.json could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return ApplicationBindingStoreReadResult.Invalid(
                _filePath,
                $"application-bindings.json could not be read: {exception.Message}");
        }
    }

    public Task<ApplicationBindingStoreWriteResult> AddAppPathAsync(
        string applicationId,
        string appPathExecutableName,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var idValidation = ApplicationBindingValidator.ValidateApplicationId(applicationId);
        if (!idValidation.IsValid)
        {
            return Task.FromResult(InvalidCandidate(idValidation));
        }

        var appPathValidation = ApplicationBindingValidator.ValidateAppPathExecutableName(appPathExecutableName);
        if (!appPathValidation.IsValid)
        {
            return Task.FromResult(InvalidCandidate(appPathValidation));
        }

        return AddOrReplaceAsync(
            idValidation.NormalizedValue!,
            now => ApplicationBinding.CreateAppPaths(
                idValidation.NormalizedValue!,
                appPathValidation.NormalizedValue!,
                now),
            replaceExisting,
            cancellationToken);
    }

    public Task<ApplicationBindingStoreWriteResult> AddAbsoluteExeAsync(
        string applicationId,
        string executablePath,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var idValidation = ApplicationBindingValidator.ValidateApplicationId(applicationId);
        if (!idValidation.IsValid)
        {
            return Task.FromResult(InvalidCandidate(idValidation));
        }

        var pathValidation = ApplicationBindingValidator.ValidateAbsoluteExePath(executablePath, requireExists: true);
        if (!pathValidation.IsValid)
        {
            return Task.FromResult(InvalidCandidate(pathValidation));
        }

        return AddOrReplaceAsync(
            idValidation.NormalizedValue!,
            now => ApplicationBinding.CreateAbsoluteExe(
                idValidation.NormalizedValue!,
                pathValidation.NormalizedValue!,
                now),
            replaceExisting,
            cancellationToken);
    }

    public async Task<ApplicationBindingStoreWriteResult> ReplaceAsync(
        ApplicationBinding binding,
        CancellationToken cancellationToken)
    {
        var bindingValidation = ApplicationBindingValidator.ValidateBinding(binding, requireAbsoluteExeExists: true);
        if (!bindingValidation.IsValid)
        {
            return InvalidCandidate(bindingValidation);
        }

        return await AddOrReplaceAsync(
            binding.ApplicationId,
            now => binding with
            {
                UpdatedAtUtc = now.ToUniversalTime()
            },
            replaceExisting: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApplicationBindingStoreWriteResult> SetEnabledAsync(
        string applicationId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var idValidation = ApplicationBindingValidator.ValidateApplicationId(applicationId);
        if (!idValidation.IsValid)
        {
            return InvalidCandidate(idValidation);
        }

        var load = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return StoreInvalid(load);
        }

        var existing = Find(load.Bindings, idValidation.NormalizedValue!);
        if (existing is null)
        {
            return NotFound();
        }

        var updated = existing.WithEnabled(enabled, _clock.UtcNow);
        var bindings = load.Bindings
            .Select(binding => SameApplicationId(binding.ApplicationId, updated.ApplicationId) ? updated : binding)
            .ToArray();

        return await PersistAsync(bindings, updated, ApplicationBindingStoreWriteStatus.Configured, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ApplicationBindingStoreWriteResult> RemoveAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        var idValidation = ApplicationBindingValidator.ValidateApplicationId(applicationId);
        if (!idValidation.IsValid)
        {
            return InvalidCandidate(idValidation);
        }

        var load = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return StoreInvalid(load);
        }

        var existing = Find(load.Bindings, idValidation.NormalizedValue!);
        if (existing is null)
        {
            return NotFound();
        }

        var bindings = load.Bindings
            .Where(binding => !SameApplicationId(binding.ApplicationId, idValidation.NormalizedValue!))
            .ToArray();

        return await PersistAsync(
            bindings,
            null,
            ApplicationBindingStoreWriteStatus.Removed,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ApplicationBindingStoreWriteResult> AddOrReplaceAsync(
        string applicationId,
        Func<DateTimeOffset, ApplicationBinding> createBinding,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var load = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return StoreInvalid(load);
        }

        var nowUtc = _clock.UtcNow;
        var candidate = createBinding(nowUtc);
        var existing = Find(load.Bindings, applicationId);

        if (existing is not null && !replaceExisting)
        {
            return ApplicationBindingStoreWriteResult.Failure(
                ApplicationBindingStoreWriteStatus.AlreadyExists,
                _filePath,
                ApplicationBindingErrorCodes.ApplicationBindingAlreadyExists,
                "Application binding already exists. Use --replace-application-binding to replace it.");
        }

        var binding = existing is null
            ? candidate
            : existing.WithUpdatedTarget(
                candidate.LaunchType,
                candidate.AppPathExecutableName,
                candidate.ExecutablePath,
                nowUtc);

        var bindings = existing is null
            ? load.Bindings.Concat([binding]).ToArray()
            : load.Bindings
                .Select(current => SameApplicationId(current.ApplicationId, applicationId) ? binding : current)
                .ToArray();

        return await PersistAsync(bindings, binding, ApplicationBindingStoreWriteStatus.Configured, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ApplicationBindingStoreWriteResult> PersistAsync(
        IReadOnlyList<ApplicationBinding> bindings,
        ApplicationBinding? returnedBinding,
        ApplicationBindingStoreWriteStatus successStatus,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);

        var document = new ApplicationBindingCatalogDocument(
            ApplicationBindingConstants.SchemaVersion,
            bindings.OrderBy(binding => binding.ApplicationId, StringComparer.OrdinalIgnoreCase).ToArray());

        var validation = ApplicationBindingValidator.ValidateCatalogDocument(document);
        if (!validation.IsValid)
        {
            return InvalidCandidate(validation);
        }

        var tempPath = Path.Combine(
            _dataDirectory,
            $"{ApplicationBindingConstants.FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(document, WriteOptions);
            await DurableFileWriter.WriteTextAsync(tempPath, json, Utf8WithoutBom, cancellationToken)
                .ConfigureAwait(false);
            _fileSecurity.Apply(tempPath);
            DurableFileWriter.ReplaceOrMove(tempPath, _filePath);
            _fileSecurity.Apply(_filePath);

            var verification = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (verification.Loaded
                && BindingsEqual(document.Bindings, verification.Bindings))
            {
                return ApplicationBindingStoreWriteResult.Success(successStatus, returnedBinding, _filePath);
            }

            return ApplicationBindingStoreWriteResult.Failure(
                ApplicationBindingStoreWriteStatus.VerificationFailed,
                _filePath,
                ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
                verification.ErrorMessage ?? "Application bindings write verification failed.");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private ApplicationBindingStoreWriteResult InvalidCandidate(ApplicationBindingValidationResult validation)
    {
        var status = validation.Status == ApplicationBindingValidationStatus.ExecutableNotFound
            ? ApplicationBindingStoreWriteStatus.ExecutableNotFound
            : ApplicationBindingStoreWriteStatus.Invalid;
        var code = validation.Status == ApplicationBindingValidationStatus.ExecutableNotFound
            ? ApplicationBindingErrorCodes.ApplicationExecutableNotFound
            : ApplicationBindingErrorCodes.ApplicationBindingInvalid;

        return ApplicationBindingStoreWriteResult.Failure(
            status,
            _filePath,
            code,
            validation.ErrorMessage ?? "Application binding is invalid.");
    }

    private ApplicationBindingStoreWriteResult StoreInvalid(ApplicationBindingStoreReadResult load)
    {
        return ApplicationBindingStoreWriteResult.Failure(
            ApplicationBindingStoreWriteStatus.StoreInvalid,
            _filePath,
            ApplicationBindingErrorCodes.ApplicationBindingsInvalid,
            load.ErrorMessage ?? "Application bindings store is invalid.");
    }

    private ApplicationBindingStoreWriteResult NotFound()
    {
        return ApplicationBindingStoreWriteResult.Failure(
            ApplicationBindingStoreWriteStatus.NotFound,
            _filePath,
            ApplicationBindingErrorCodes.ApplicationBindingNotFound,
            "Application binding was not found.");
    }

    private static ApplicationBinding? Find(
        IEnumerable<ApplicationBinding> bindings,
        string applicationId)
    {
        return bindings.FirstOrDefault(binding => SameApplicationId(binding.ApplicationId, applicationId));
    }

    private static bool SameApplicationId(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool BindingsEqual(
        IReadOnlyList<ApplicationBinding> expected,
        IReadOnlyList<ApplicationBinding> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (expected[index] != actual[index])
            {
                return false;
            }
        }

        return true;
    }
}
