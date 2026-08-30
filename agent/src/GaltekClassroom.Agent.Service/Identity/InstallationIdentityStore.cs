using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Persistence;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Identity;

public sealed record InstallationIdentityStoreOptions(string DataDirectory);

public enum InstallationIdentityStoreReadStatus
{
    Missing,
    Loaded,
    Invalid
}

public sealed record InstallationIdentityStoreReadResult(
    InstallationIdentityStoreReadStatus Status,
    InstallationIdentity? Identity,
    string FilePath,
    string? ErrorMessage)
{
    public static InstallationIdentityStoreReadResult Missing(string filePath)
    {
        return new InstallationIdentityStoreReadResult(
            InstallationIdentityStoreReadStatus.Missing,
            null,
            filePath,
            null);
    }

    public static InstallationIdentityStoreReadResult Loaded(InstallationIdentity identity, string filePath)
    {
        return new InstallationIdentityStoreReadResult(
            InstallationIdentityStoreReadStatus.Loaded,
            identity,
            filePath,
            null);
    }

    public static InstallationIdentityStoreReadResult Invalid(string filePath, string errorMessage)
    {
        return new InstallationIdentityStoreReadResult(
            InstallationIdentityStoreReadStatus.Invalid,
            null,
            filePath,
            errorMessage);
    }
}

public sealed class InstallationIdentityStore
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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

    public InstallationIdentityStore(InstallationIdentityStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, InstallationIdentityConstants.FileName);
    }

    public string FilePath => _filePath;

    public async Task<InstallationIdentityStoreReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return InstallationIdentityStoreReadResult.Missing(_filePath);
        }

        try
        {
            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            var identity = await JsonSerializer.DeserializeAsync<InstallationIdentity>(
                stream,
                ReadOptions,
                cancellationToken);

            if (!InstallationIdentityValidator.IsValid(identity, out var validationError))
            {
                return InstallationIdentityStoreReadResult.Invalid(
                    _filePath,
                    $"installation.json is corrupt or incomplete: {validationError}");
            }

            return InstallationIdentityStoreReadResult.Loaded(identity!, _filePath);
        }
        catch (JsonException exception)
        {
            return InstallationIdentityStoreReadResult.Invalid(
                _filePath,
                $"installation.json is corrupt or incomplete: {exception.Message}");
        }
        catch (IOException exception)
        {
            return InstallationIdentityStoreReadResult.Invalid(
                _filePath,
                $"installation.json could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return InstallationIdentityStoreReadResult.Invalid(
                _filePath,
                $"installation.json could not be read: {exception.Message}");
        }
    }

    public async Task WriteNewAsync(InstallationIdentity identity, CancellationToken cancellationToken)
    {
        if (!InstallationIdentityValidator.IsValid(identity, out var validationError))
        {
            throw new InvalidOperationException($"Cannot persist invalid installation identity: {validationError}");
        }

        Directory.CreateDirectory(_dataDirectory);

        var tempPath = Path.Combine(
            _dataDirectory,
            $"{InstallationIdentityConstants.FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(identity, WriteOptions);
            await DurableFileWriter.WriteTextAsync(tempPath, json, Utf8WithoutBom, cancellationToken);
            DurableFileWriter.ReplaceOrMove(tempPath, _filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
