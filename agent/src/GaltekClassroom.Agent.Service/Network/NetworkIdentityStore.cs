using System.Text.Json;

namespace GaltekClassroom.Agent.Service.Network;

public sealed record NetworkIdentityStoreOptions(string DataDirectory);

public enum NetworkIdentityStoreReadStatus
{
    Missing,
    Loaded,
    Invalid
}

public sealed record NetworkIdentityStoreReadResult(
    NetworkIdentityStoreReadStatus Status,
    NetworkIdentityMetadata? Metadata,
    string FilePath,
    string? ErrorMessage)
{
    public static NetworkIdentityStoreReadResult Missing(string filePath)
    {
        return new NetworkIdentityStoreReadResult(
            NetworkIdentityStoreReadStatus.Missing,
            null,
            filePath,
            null);
    }

    public static NetworkIdentityStoreReadResult Loaded(
        NetworkIdentityMetadata metadata,
        string filePath)
    {
        return new NetworkIdentityStoreReadResult(
            NetworkIdentityStoreReadStatus.Loaded,
            metadata,
            filePath,
            null);
    }

    public static NetworkIdentityStoreReadResult Invalid(string filePath, string errorMessage)
    {
        return new NetworkIdentityStoreReadResult(
            NetworkIdentityStoreReadStatus.Invalid,
            null,
            filePath,
            errorMessage);
    }
}

public sealed class NetworkIdentityStore
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
    private readonly INetworkIdentityFileSecurity _fileSecurity;

    public NetworkIdentityStore(
        NetworkIdentityStoreOptions options,
        INetworkIdentityFileSecurity fileSecurity)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSecurity);

        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, NetworkIdentityConstants.FileName);
        _fileSecurity = fileSecurity;
    }

    public string FilePath => _filePath;

    public async Task<NetworkIdentityStoreReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return NetworkIdentityStoreReadResult.Missing(_filePath);
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

            var metadata = await JsonSerializer.DeserializeAsync<NetworkIdentityMetadata>(
                stream,
                ReadOptions,
                cancellationToken);

            if (!NetworkIdentityValidator.IsValid(metadata, out var validationError))
            {
                return NetworkIdentityStoreReadResult.Invalid(
                    _filePath,
                    $"network-identity.json is corrupt or incomplete: {validationError}");
            }

            return NetworkIdentityStoreReadResult.Loaded(metadata!, _filePath);
        }
        catch (JsonException exception)
        {
            return NetworkIdentityStoreReadResult.Invalid(
                _filePath,
                $"network-identity.json is corrupt or incomplete: {exception.Message}");
        }
        catch (IOException exception)
        {
            return NetworkIdentityStoreReadResult.Invalid(
                _filePath,
                $"network-identity.json could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return NetworkIdentityStoreReadResult.Invalid(
                _filePath,
                $"network-identity.json could not be read: {exception.Message}");
        }
    }

    public async Task WriteNewAsync(
        NetworkIdentityMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!NetworkIdentityValidator.IsValid(metadata, out var validationError))
        {
            throw new InvalidOperationException($"Cannot persist invalid network identity: {validationError}");
        }

        Directory.CreateDirectory(_dataDirectory);

        if (File.Exists(_filePath))
        {
            throw new InvalidOperationException("network-identity.json already exists; refusing to overwrite it.");
        }

        var tempPath = Path.Combine(
            _dataDirectory,
            $"{NetworkIdentityConstants.FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteCandidateAsync(tempPath, metadata, cancellationToken);
            _fileSecurity.Apply(tempPath);
            File.Move(tempPath, _filePath);
            _fileSecurity.Apply(_filePath);
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
        NetworkIdentityMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        await JsonSerializer.SerializeAsync(stream, metadata, WriteOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }
}
