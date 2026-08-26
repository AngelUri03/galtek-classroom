using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Master;

public sealed record MasterBindingStoreOptions(string DataDirectory);

public enum MasterBindingStoreReadStatus
{
    Missing,
    Loaded,
    Invalid
}

public sealed record MasterBindingStoreReadResult(
    MasterBindingStoreReadStatus Status,
    MasterWindowsBinding? Binding,
    string FilePath,
    string? ErrorMessage)
{
    public static MasterBindingStoreReadResult Missing(string filePath)
    {
        return new MasterBindingStoreReadResult(
            MasterBindingStoreReadStatus.Missing,
            null,
            filePath,
            null);
    }

    public static MasterBindingStoreReadResult Loaded(
        MasterWindowsBinding binding,
        string filePath)
    {
        return new MasterBindingStoreReadResult(
            MasterBindingStoreReadStatus.Loaded,
            binding,
            filePath,
            null);
    }

    public static MasterBindingStoreReadResult Invalid(string filePath, string errorMessage)
    {
        return new MasterBindingStoreReadResult(
            MasterBindingStoreReadStatus.Invalid,
            null,
            filePath,
            errorMessage);
    }
}

public enum MasterBindingStoreWriteStatus
{
    Configured,
    AlreadyConfigured,
    InvalidCandidate,
    VerificationFailed
}

public sealed record MasterBindingStoreWriteResult(
    MasterBindingStoreWriteStatus Status,
    MasterWindowsBinding? Binding,
    string FilePath,
    string? ErrorMessage)
{
    public bool Configured => Status == MasterBindingStoreWriteStatus.Configured;
}

public sealed class MasterBindingStore
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _dataDirectory;
    private readonly string _filePath;
    private readonly IMasterBindingFileSecurity _fileSecurity;

    public MasterBindingStore(
        MasterBindingStoreOptions options,
        IMasterBindingFileSecurity fileSecurity)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSecurity);

        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, MasterBindingConstants.FileName);
        _fileSecurity = fileSecurity;
    }

    public string FilePath => _filePath;

    public async Task<MasterBindingStoreReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return MasterBindingStoreReadResult.Missing(_filePath);
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

            var binding = await JsonSerializer.DeserializeAsync<MasterWindowsBinding>(
                stream,
                ReadOptions,
                cancellationToken);

            if (!MasterBindingValidator.IsValid(binding, out var validationError))
            {
                return MasterBindingStoreReadResult.Invalid(_filePath, validationError);
            }

            return MasterBindingStoreReadResult.Loaded(binding!, _filePath);
        }
        catch (JsonException exception)
        {
            return MasterBindingStoreReadResult.Invalid(
                _filePath,
                $"master-binding.json is corrupt or incomplete: {exception.Message}");
        }
        catch (IOException exception)
        {
            return MasterBindingStoreReadResult.Invalid(
                _filePath,
                $"master-binding.json could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return MasterBindingStoreReadResult.Invalid(
                _filePath,
                $"master-binding.json could not be read: {exception.Message}");
        }
    }

    public async Task<MasterBindingStoreWriteResult> WriteAsync(
        MasterWindowsBinding candidate,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        if (!MasterBindingValidator.IsValid(candidate, out var validationError))
        {
            return new MasterBindingStoreWriteResult(
                MasterBindingStoreWriteStatus.InvalidCandidate,
                null,
                _filePath,
                validationError);
        }

        Directory.CreateDirectory(_dataDirectory);

        if (File.Exists(_filePath) && !replaceExisting)
        {
            return new MasterBindingStoreWriteResult(
                MasterBindingStoreWriteStatus.AlreadyConfigured,
                null,
                _filePath,
                "Master binding already exists. Use --replace-master-binding to replace it.");
        }

        var tempPath = Path.Combine(
            _dataDirectory,
            $"{MasterBindingConstants.FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteCandidateAsync(tempPath, candidate, cancellationToken);
            _fileSecurity.Apply(tempPath);

            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }

            _fileSecurity.Apply(_filePath);

            var verification = await ReadAsync(cancellationToken);
            if (verification.Status == MasterBindingStoreReadStatus.Loaded
                && BindingEquals(candidate, verification.Binding!))
            {
                return new MasterBindingStoreWriteResult(
                    MasterBindingStoreWriteStatus.Configured,
                    verification.Binding,
                    _filePath,
                    null);
            }

            return new MasterBindingStoreWriteResult(
                MasterBindingStoreWriteStatus.VerificationFailed,
                null,
                _filePath,
                verification.ErrorMessage ?? "Master binding write verification failed.");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static async Task WriteCandidateAsync(
        string tempPath,
        MasterWindowsBinding candidate,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        await JsonSerializer.SerializeAsync(stream, candidate, WriteOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static bool BindingEquals(MasterWindowsBinding expected, MasterWindowsBinding actual)
    {
        return expected.SchemaVersion == actual.SchemaVersion
            && expected.InstallationId == actual.InstallationId
            && string.Equals(expected.WindowsSid, actual.WindowsSid, StringComparison.OrdinalIgnoreCase)
            && string.Equals(expected.AccountDisplayName, actual.AccountDisplayName, StringComparison.Ordinal)
            && expected.BoundAtUtc.ToUniversalTime() == actual.BoundAtUtc.ToUniversalTime();
    }
}
