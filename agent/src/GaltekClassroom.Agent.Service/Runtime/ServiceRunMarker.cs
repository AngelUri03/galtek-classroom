using System.Globalization;
using System.Text;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Persistence;

namespace GaltekClassroom.Agent.Service.Runtime;

public sealed record ServiceRunMarkerOptions(string DataDirectory, string FileName);

public sealed record ServiceRunMarkerStartResult(
    bool PreviousShutdownWasUnclean,
    bool MarkerWritten,
    string FilePath,
    DateTimeOffset StartedAtUtc,
    string? ErrorMessage);

public sealed record ServiceRunMarkerStopResult(
    bool Removed,
    string FilePath,
    string? ErrorMessage);

public sealed class ServiceRunMarker
{
    public const string DefaultFileName = "agent-service.running";

    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _dataDirectory;
    private readonly string _filePath;
    private readonly ISystemClock _clock;

    public ServiceRunMarker(ServiceRunMarkerOptions options, ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _dataDirectory = Path.GetFullPath(options.DataDirectory);
        _filePath = Path.Combine(_dataDirectory, options.FileName);
        _clock = clock;
    }

    public string FilePath => _filePath;

    public async Task<ServiceRunMarkerStartResult> MarkStartedAsync(CancellationToken cancellationToken)
    {
        var previousShutdownWasUnclean = File.Exists(_filePath);
        var startedAtUtc = _clock.UtcNow.ToUniversalTime();

        try
        {
            Directory.CreateDirectory(_dataDirectory);
            var tempPath = Path.Combine(_dataDirectory, $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await DurableFileWriter.WriteTextAsync(
                    tempPath,
                    CreateMarkerContents(startedAtUtc),
                    Utf8WithoutBom,
                    cancellationToken);
                DurableFileWriter.ReplaceOrMove(tempPath, _filePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            return new ServiceRunMarkerStartResult(
                previousShutdownWasUnclean,
                MarkerWritten: true,
                _filePath,
                startedAtUtc,
                null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return new ServiceRunMarkerStartResult(
                previousShutdownWasUnclean,
                MarkerWritten: false,
                _filePath,
                startedAtUtc,
                exception.Message);
        }
    }

    public Task<ServiceRunMarkerStopResult> MarkStoppedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            return Task.FromResult(new ServiceRunMarkerStopResult(
                Removed: true,
                _filePath,
                null));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return Task.FromResult(new ServiceRunMarkerStopResult(
                Removed: false,
                _filePath,
                exception.Message));
        }
    }

    private static string CreateMarkerContents(DateTimeOffset startedAtUtc)
    {
        return string.Join(
            Environment.NewLine,
            "schemaVersion=1",
            "process=GaltekClassroom.Agent.Service",
            "startedAtUtc=" + startedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            string.Empty);
    }
}
