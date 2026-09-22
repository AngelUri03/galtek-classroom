using System.Text;
using GaltekClassroom.Agent.Service.Identity;

namespace GaltekClassroom.Agent.Service.Diagnostics;

public static class BootstrapStartupStages
{
    public const string ProcessEnter = "PROCESS_ENTER";
    public const string CreateBuilder = "CREATE_BUILDER";
    public const string Configuration = "CONFIG";
    public const string DiBuild = "DI_BUILD";
    public const string HostRun = "HOST_RUN";
}

public sealed class BootstrapStartupDiagnostics
{
    public const string EnabledEnvironmentVariable = "GALTEK_BOOTSTRAP_STARTUP_DIAGNOSTICS";
    public const string DefaultRelativePath = "Diagnostics/startup-bootstrap.log";
    public const long DefaultMaximumBytes = 64 * 1024;

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly string? _filePath;
    private readonly long _maximumBytes;
    private readonly object _writeGate = new();

    public BootstrapStartupDiagnostics(string? filePath, long maximumBytes = DefaultMaximumBytes)
    {
        _filePath = filePath;
        _maximumBytes = Math.Max(512, maximumBytes);
    }

    public static BootstrapStartupDiagnostics CreateDefault()
    {
        return CreateDefaultForValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
    }

    public static BootstrapStartupDiagnostics CreateDefaultForValue(
        string? enabledValue,
        Func<string>? dataDirectoryResolver = null)
    {
        if (!IsExplicitlyEnabled(enabledValue))
        {
            return new BootstrapStartupDiagnostics(filePath: null);
        }

        try
        {
            return new BootstrapStartupDiagnostics(
                Path.Combine(
                    (dataDirectoryResolver ?? (() => AgentDataDirectory.Resolve()))(),
                    "Diagnostics",
                    "startup-bootstrap.log"));
        }
        catch
        {
            return new BootstrapStartupDiagnostics(filePath: null);
        }
    }

    public static bool IsExplicitlyEnabled(string? value)
    {
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public void Write(string marker)
    {
        WriteCore(SanitizeToken(marker, 96));
    }

    public void WriteFatal(string stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        WriteCore(
            $"BOOTSTRAP_FATAL stage={SanitizeToken(stage, 64)} " +
            $"exceptionType={SanitizeToken(exceptionType, 160)} " +
            $"hresult=0x{exception.HResult:X8}");
    }

    private void WriteCore(string payload)
    {
        if (string.IsNullOrWhiteSpace(_filePath)
            || !Monitor.TryEnter(_writeGate))
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.Now;
            var line =
                $"utc={now.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'} " +
                $"local={now:O} {payload}{Environment.NewLine}";
            var bytes = Utf8WithoutBom.GetBytes(line);
            var directory = Path.GetDirectoryName(_filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            using var stream = new FileStream(
                _filePath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.WriteThrough);

            if (stream.Length + bytes.Length > _maximumBytes)
            {
                stream.SetLength(0);
            }

            stream.Position = stream.Length;
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            // Bootstrap diagnostics must never alter service startup or failure semantics.
        }
        finally
        {
            Monitor.Exit(_writeGate);
        }
    }

    private static string SanitizeToken(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        var bounded = value.Length <= maximumLength
            ? value
            : value[..maximumLength];
        var sanitized = new char[bounded.Length];

        for (var index = 0; index < bounded.Length; index++)
        {
            var character = bounded[index];
            sanitized[index] = char.IsAsciiLetterOrDigit(character)
                || character is '_' or '-' or '.' or '+' or '`'
                    ? character
                    : '_';
        }

        return new string(sanitized);
    }
}
