using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Persistence;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public sealed record ManagedWindowsAccountBindingStoreOptions(string DataDirectory);

public enum ManagedWindowsAccountBindingStoreReadStatus
{
    Loaded,
    Invalid
}

public sealed record ManagedWindowsAccountBindingStoreReadResult(
    ManagedWindowsAccountBindingStoreReadStatus Status,
    IReadOnlyList<ManagedWindowsAccountBinding> Bindings,
    string FilePath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Loaded => Status == ManagedWindowsAccountBindingStoreReadStatus.Loaded;

    public static ManagedWindowsAccountBindingStoreReadResult LoadedBindings(
        IReadOnlyList<ManagedWindowsAccountBinding> bindings,
        string filePath)
    {
        return new ManagedWindowsAccountBindingStoreReadResult(
            ManagedWindowsAccountBindingStoreReadStatus.Loaded,
            bindings,
            filePath,
            null,
            null);
    }

    public static ManagedWindowsAccountBindingStoreReadResult Invalid(
        string filePath,
        string errorMessage)
    {
        return new ManagedWindowsAccountBindingStoreReadResult(
            ManagedWindowsAccountBindingStoreReadStatus.Invalid,
            Array.Empty<ManagedWindowsAccountBinding>(),
            filePath,
            ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingsInvalid,
            errorMessage);
    }
}

public enum ManagedWindowsAccountBindingStoreWriteStatus
{
    Configured,
    Removed,
    AlreadyExists,
    NotFound,
    Conflict,
    Invalid,
    StoreInvalid,
    VerificationFailed
}

public sealed record ManagedWindowsAccountBindingStoreWriteResult(
    ManagedWindowsAccountBindingStoreWriteStatus Status,
    ManagedWindowsAccountBinding? Binding,
    string FilePath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Succeeded => Status is ManagedWindowsAccountBindingStoreWriteStatus.Configured
        or ManagedWindowsAccountBindingStoreWriteStatus.Removed;

    public static ManagedWindowsAccountBindingStoreWriteResult Success(
        ManagedWindowsAccountBindingStoreWriteStatus status,
        ManagedWindowsAccountBinding? binding,
        string filePath)
    {
        return new ManagedWindowsAccountBindingStoreWriteResult(status, binding, filePath, null, null);
    }

    public static ManagedWindowsAccountBindingStoreWriteResult Failure(
        ManagedWindowsAccountBindingStoreWriteStatus status,
        string filePath,
        string errorCode,
        string errorMessage)
    {
        return new ManagedWindowsAccountBindingStoreWriteResult(status, null, filePath, errorCode, errorMessage);
    }
}

public interface IManagedWindowsAccountBindingStore
{
    string FilePath { get; }

    Task<ManagedWindowsAccountBindingStoreReadResult> LoadAsync(
        Guid currentInstallationId,
        CancellationToken cancellationToken);

    Task<ManagedWindowsAccountBindingStoreReadResult> ListAsync(
        Guid currentInstallationId,
        CancellationToken cancellationToken);

    Task<ManagedWindowsAccountBindingStoreReadResult> GetAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken);

    Task<ManagedWindowsAccountBindingStoreWriteResult> AddAsync(
        Guid currentInstallationId,
        ManagedWindowsAccountBinding binding,
        CancellationToken cancellationToken);

    Task<ManagedWindowsAccountBindingStoreWriteResult> ReplaceAsync(
        Guid currentInstallationId,
        ManagedWindowsAccountBinding binding,
        CancellationToken cancellationToken);

    Task<ManagedWindowsAccountBindingStoreWriteResult> RemoveAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken);
}

public sealed class ManagedWindowsAccountBindingStore : IManagedWindowsAccountBindingStore
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ManagedWindowsAccountBindingDocumentGuardConverter() }
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _dataDirectory;
    private readonly string _filePath;
    private readonly IManagedWindowsAccountBindingFileSecurity _fileSecurity;

    public ManagedWindowsAccountBindingStore(
        ManagedWindowsAccountBindingStoreOptions options,
        IManagedWindowsAccountBindingFileSecurity fileSecurity)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSecurity);

        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, ManagedWindowsAccountBindingConstants.FileName);
        _fileSecurity = fileSecurity;
    }

    public string FilePath => _filePath;

    public Task<ManagedWindowsAccountBindingStoreReadResult> ListAsync(
        Guid currentInstallationId,
        CancellationToken cancellationToken)
    {
        return LoadAsync(currentInstallationId, cancellationToken);
    }

    public async Task<ManagedWindowsAccountBindingStoreReadResult> GetAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken)
    {
        var idValidation = ManagedWindowsAccountBindingValidator.ValidateAccountId(accountId);
        if (!idValidation.IsValid)
        {
            return ManagedWindowsAccountBindingStoreReadResult.Invalid(
                _filePath,
                idValidation.ErrorMessage ?? "Managed Windows accountId is invalid.");
        }

        var normalizedAccountId = ManagedWindowsAccountBinding.NormalizeAccountId(accountId);
        var load = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return load;
        }

        var binding = Find(load.Bindings, normalizedAccountId);
        return ManagedWindowsAccountBindingStoreReadResult.LoadedBindings(
            binding is null ? Array.Empty<ManagedWindowsAccountBinding>() : [binding],
            _filePath);
    }

    public async Task<ManagedWindowsAccountBindingStoreReadResult> LoadAsync(
        Guid currentInstallationId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return ManagedWindowsAccountBindingStoreReadResult.LoadedBindings(
                Array.Empty<ManagedWindowsAccountBinding>(),
                _filePath);
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

            var document = await JsonSerializer.DeserializeAsync<ManagedWindowsAccountBindingCatalogDocument>(
                stream,
                ReadOptions,
                cancellationToken).ConfigureAwait(false);

            var validation = ManagedWindowsAccountBindingValidator.ValidateCatalogDocument(
                document,
                currentInstallationId);
            if (!validation.IsValid)
            {
                return ManagedWindowsAccountBindingStoreReadResult.Invalid(
                    _filePath,
                    validation.ErrorMessage ?? "managed-windows-accounts.json is invalid.");
            }

            return ManagedWindowsAccountBindingStoreReadResult.LoadedBindings(
                OrderBindings(document!.Bindings),
                _filePath);
        }
        catch (JsonException exception)
        {
            return ManagedWindowsAccountBindingStoreReadResult.Invalid(
                _filePath,
                $"managed-windows-accounts.json is corrupt or incomplete: {exception.Message}");
        }
        catch (IOException exception)
        {
            return ManagedWindowsAccountBindingStoreReadResult.Invalid(
                _filePath,
                $"managed-windows-accounts.json could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return ManagedWindowsAccountBindingStoreReadResult.Invalid(
                _filePath,
                $"managed-windows-accounts.json could not be read: {exception.Message}");
        }
    }

    public Task<ManagedWindowsAccountBindingStoreWriteResult> AddAsync(
        Guid currentInstallationId,
        ManagedWindowsAccountBinding binding,
        CancellationToken cancellationToken)
    {
        return AddOrReplaceAsync(currentInstallationId, binding, replaceExisting: false, cancellationToken);
    }

    public Task<ManagedWindowsAccountBindingStoreWriteResult> ReplaceAsync(
        Guid currentInstallationId,
        ManagedWindowsAccountBinding binding,
        CancellationToken cancellationToken)
    {
        return AddOrReplaceAsync(currentInstallationId, binding, replaceExisting: true, cancellationToken);
    }

    public async Task<ManagedWindowsAccountBindingStoreWriteResult> RemoveAsync(
        Guid currentInstallationId,
        string accountId,
        CancellationToken cancellationToken)
    {
        var idValidation = ManagedWindowsAccountBindingValidator.ValidateAccountId(accountId);
        if (!idValidation.IsValid)
        {
            return InvalidCandidate(idValidation);
        }

        var normalizedAccountId = ManagedWindowsAccountBinding.NormalizeAccountId(accountId);
        var load = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return StoreInvalid(load);
        }

        var existing = Find(load.Bindings, normalizedAccountId);
        if (existing is null)
        {
            return NotFound();
        }

        var bindings = load.Bindings
            .Where(binding => !SameAccountId(binding.AccountId, normalizedAccountId))
            .ToArray();

        return await PersistAsync(
            currentInstallationId,
            bindings,
            null,
            ManagedWindowsAccountBindingStoreWriteStatus.Removed,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedWindowsAccountBindingStoreWriteResult> AddOrReplaceAsync(
        Guid currentInstallationId,
        ManagedWindowsAccountBinding candidate,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var bindingValidation = ManagedWindowsAccountBindingValidator.ValidateBinding(candidate);
        if (!bindingValidation.IsValid)
        {
            return InvalidCandidate(bindingValidation);
        }

        var load = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
        if (!load.Loaded)
        {
            return StoreInvalid(load);
        }

        var existing = Find(load.Bindings, candidate.AccountId);
        if (existing is not null && !replaceExisting)
        {
            return ManagedWindowsAccountBindingStoreWriteResult.Failure(
                ManagedWindowsAccountBindingStoreWriteStatus.AlreadyExists,
                _filePath,
                ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingAlreadyExists,
                "Managed Windows account binding already exists. Use --replace-managed-account-binding to replace it.");
        }

        var conflicting = load.Bindings.FirstOrDefault(binding =>
            !SameAccountId(binding.AccountId, candidate.AccountId)
            && string.Equals(binding.WindowsSid, candidate.WindowsSid, StringComparison.OrdinalIgnoreCase));
        if (conflicting is not null)
        {
            return ManagedWindowsAccountBindingStoreWriteResult.Failure(
                ManagedWindowsAccountBindingStoreWriteStatus.Conflict,
                _filePath,
                ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingConflict,
                $"{candidate.AccountId} cannot bind to the same Windows SID as {conflicting.AccountId}.");
        }

        var binding = existing is null
            ? candidate
            : existing.WithUpdatedTarget(
                candidate.WindowsSid,
                candidate.AccountReference,
                candidate.UpdatedAtUtc);

        var bindings = existing is null
            ? load.Bindings.Concat([binding]).ToArray()
            : load.Bindings
                .Select(current => SameAccountId(current.AccountId, candidate.AccountId) ? binding : current)
                .ToArray();

        return await PersistAsync(
            currentInstallationId,
            bindings,
            binding,
            ManagedWindowsAccountBindingStoreWriteStatus.Configured,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedWindowsAccountBindingStoreWriteResult> PersistAsync(
        Guid currentInstallationId,
        IReadOnlyList<ManagedWindowsAccountBinding> bindings,
        ManagedWindowsAccountBinding? returnedBinding,
        ManagedWindowsAccountBindingStoreWriteStatus successStatus,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);

        var document = new ManagedWindowsAccountBindingCatalogDocument(
            ManagedWindowsAccountBindingConstants.SchemaVersion,
            currentInstallationId,
            OrderBindings(bindings));

        var validation = ManagedWindowsAccountBindingValidator.ValidateCatalogDocument(
            document,
            currentInstallationId);
        if (!validation.IsValid)
        {
            return InvalidCandidate(validation);
        }

        var tempPath = Path.Combine(
            _dataDirectory,
            $"{ManagedWindowsAccountBindingConstants.FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(document, WriteOptions);
            await DurableFileWriter.WriteTextAsync(tempPath, json, Utf8WithoutBom, cancellationToken)
                .ConfigureAwait(false);
            _fileSecurity.Apply(tempPath);
            DurableFileWriter.ReplaceOrMove(tempPath, _filePath);
            _fileSecurity.Apply(_filePath);

            var verification = await LoadAsync(currentInstallationId, cancellationToken).ConfigureAwait(false);
            if (verification.Loaded
                && BindingsEqual(document.Bindings, verification.Bindings))
            {
                return ManagedWindowsAccountBindingStoreWriteResult.Success(
                    successStatus,
                    returnedBinding,
                    _filePath);
            }

            return ManagedWindowsAccountBindingStoreWriteResult.Failure(
                ManagedWindowsAccountBindingStoreWriteStatus.VerificationFailed,
                _filePath,
                ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingsInvalid,
                verification.ErrorMessage ?? "Managed Windows account bindings write verification failed.");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private ManagedWindowsAccountBindingStoreWriteResult InvalidCandidate(
        ManagedWindowsAccountBindingValidationResult validation)
    {
        return ManagedWindowsAccountBindingStoreWriteResult.Failure(
            ManagedWindowsAccountBindingStoreWriteStatus.Invalid,
            _filePath,
            ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingInvalid,
            validation.ErrorMessage ?? "Managed Windows account binding is invalid.");
    }

    private ManagedWindowsAccountBindingStoreWriteResult StoreInvalid(
        ManagedWindowsAccountBindingStoreReadResult load)
    {
        return ManagedWindowsAccountBindingStoreWriteResult.Failure(
            ManagedWindowsAccountBindingStoreWriteStatus.StoreInvalid,
            _filePath,
            ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingsInvalid,
            load.ErrorMessage ?? "Managed Windows account bindings store is invalid.");
    }

    private ManagedWindowsAccountBindingStoreWriteResult NotFound()
    {
        return ManagedWindowsAccountBindingStoreWriteResult.Failure(
            ManagedWindowsAccountBindingStoreWriteStatus.NotFound,
            _filePath,
            ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingNotFound,
            "Managed Windows account binding was not found.");
    }

    private static ManagedWindowsAccountBinding? Find(
        IEnumerable<ManagedWindowsAccountBinding> bindings,
        string accountId)
    {
        return bindings.FirstOrDefault(binding => SameAccountId(binding.AccountId, accountId));
    }

    private static bool SameAccountId(string left, string right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static ManagedWindowsAccountBinding[] OrderBindings(
        IEnumerable<ManagedWindowsAccountBinding> bindings)
    {
        return bindings
            .OrderBy(binding => binding.AccountId == ClassroomManagedWindowsAccountTypes.Primary ? 0 : 1)
            .ToArray();
    }

    private static bool BindingsEqual(
        IReadOnlyList<ManagedWindowsAccountBinding> expected,
        IReadOnlyList<ManagedWindowsAccountBinding> actual)
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
