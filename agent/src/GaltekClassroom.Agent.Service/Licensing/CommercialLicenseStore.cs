using System.Text;
using GaltekClassroom.Agent.Service.Persistence;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed record CommercialLicenseStoreOptions(string DataDirectory);

public enum CommercialLicenseStoreReadStatus
{
    Missing,
    Loaded,
    Invalid
}

public sealed record CommercialLicenseStoreReadResult(
    CommercialLicenseStoreReadStatus Status,
    string? Token,
    string FilePath,
    string? ErrorMessage)
{
    public static CommercialLicenseStoreReadResult Missing(string filePath)
    {
        return new CommercialLicenseStoreReadResult(
            CommercialLicenseStoreReadStatus.Missing,
            null,
            filePath,
            null);
    }

    public static CommercialLicenseStoreReadResult Loaded(string token, string filePath)
    {
        return new CommercialLicenseStoreReadResult(
            CommercialLicenseStoreReadStatus.Loaded,
            token,
            filePath,
            null);
    }

    public static CommercialLicenseStoreReadResult Invalid(string filePath, string errorMessage)
    {
        return new CommercialLicenseStoreReadResult(
            CommercialLicenseStoreReadStatus.Invalid,
            null,
            filePath,
            errorMessage);
    }
}

public sealed class CommercialLicenseStore
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _dataDirectory;
    private readonly string _filePath;

    public CommercialLicenseStore(CommercialLicenseStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, CommercialLicenseConstants.FileName);
    }

    public string FilePath => _filePath;

    public async Task<CommercialLicenseStoreReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return CommercialLicenseStoreReadResult.Missing(_filePath);
        }

        try
        {
            var token = await File.ReadAllTextAsync(_filePath, Utf8WithoutBom, cancellationToken);
            token = token.Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                return CommercialLicenseStoreReadResult.Invalid(
                    _filePath,
                    "license.dat is empty");
            }

            return CommercialLicenseStoreReadResult.Loaded(token, _filePath);
        }
        catch (IOException exception)
        {
            return CommercialLicenseStoreReadResult.Invalid(
                _filePath,
                $"license.dat could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return CommercialLicenseStoreReadResult.Invalid(
                _filePath,
                $"license.dat could not be read: {exception.Message}");
        }
    }

    public async Task WriteAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Commercial license token cannot be empty.", nameof(token));
        }

        Directory.CreateDirectory(_dataDirectory);

        var tempPath = Path.Combine(
            _dataDirectory,
            $"{CommercialLicenseConstants.FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await DurableFileWriter.WriteTextAsync(tempPath, token.Trim(), Utf8WithoutBom, cancellationToken);
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
