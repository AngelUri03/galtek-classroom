using System.Text;
using GaltekClassroom.Agent.Service.Diagnostics;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class BootstrapStartupDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.Bootstrap.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateDefaultForValue_WhenNotExplicitlyEnabled_DoesNotResolveOrWrite()
    {
        var resolverWasCalled = false;
        var diagnostics = BootstrapStartupDiagnostics.CreateDefaultForValue(
            enabledValue: null,
            dataDirectoryResolver: () =>
            {
                resolverWasCalled = true;
                return _directory;
            });

        diagnostics.Write("BOOTSTRAP_PROCESS_ENTER");

        Assert.False(resolverWasCalled);
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void CreateDefaultForValue_WhenExplicitlyEnabled_WritesDiagnostic(string enabledValue)
    {
        var diagnostics = BootstrapStartupDiagnostics.CreateDefaultForValue(
            enabledValue,
            () => _directory);

        diagnostics.Write("BOOTSTRAP_PROCESS_ENTER");

        var path = Path.Combine(_directory, "Diagnostics", "startup-bootstrap.log");
        Assert.Contains("BOOTSTRAP_PROCESS_ENTER", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData(" true ")]
    public void IsExplicitlyEnabled_RejectsImplicitOrMalformedValues(string value)
    {
        Assert.False(BootstrapStartupDiagnostics.IsExplicitlyEnabled(value));
    }

    [Fact]
    public void Write_WhenDestinationCannotBeOpened_DoesNotThrow()
    {
        Directory.CreateDirectory(_directory);
        var directoryUsedAsFile = Path.Combine(_directory, "blocked");
        Directory.CreateDirectory(directoryUsedAsFile);
        var diagnostics = new BootstrapStartupDiagnostics(directoryUsedAsFile);

        var exception = Record.Exception(() => diagnostics.Write("BOOTSTRAP_PROCESS_ENTER"));

        Assert.Null(exception);
    }

    [Fact]
    public void WriteFatal_DoesNotPersistExceptionMessageOrSecrets()
    {
        var path = CreateLogPath();
        var diagnostics = new BootstrapStartupDiagnostics(path);
        const string secret = "password=SuperSecret jwt=eyJ.private-key-material";

        diagnostics.WriteFatal(
            BootstrapStartupStages.DiBuild,
            new InvalidOperationException(secret));

        var contents = File.ReadAllText(path);
        Assert.Contains("BOOTSTRAP_FATAL stage=DI_BUILD", contents, StringComparison.Ordinal);
        Assert.Contains("exceptionType=System.InvalidOperationException", contents, StringComparison.Ordinal);
        Assert.Contains("hresult=0x", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperSecret", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("private-key-material", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("password=", contents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_BoundsFileAndKeepsCompleteLatestRecord()
    {
        var path = CreateLogPath();
        const long maximumBytes = 512;
        var diagnostics = new BootstrapStartupDiagnostics(path, maximumBytes);

        for (var index = 0; index < 100; index++)
        {
            diagnostics.Write($"BOOTSTRAP_TEST_{index:D3}");
        }

        var bytes = File.ReadAllBytes(path);
        var contents = Encoding.UTF8.GetString(bytes);
        Assert.InRange(bytes.LongLength, 1, maximumBytes);
        Assert.Contains("BOOTSTRAP_TEST_099", contents, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, contents, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RecordsExplicitUtcAndOffsetBearingLocalTimestamps()
    {
        var path = CreateLogPath();
        var diagnostics = new BootstrapStartupDiagnostics(path);

        diagnostics.Write("BOOTSTRAP_PROCESS_ENTER");

        var contents = File.ReadAllText(path);
        Assert.Matches(
            @"^utc=\d{4}-\d{2}-\d{2}T.+Z local=\d{4}-\d{2}-\d{2}T.+[+-]\d{2}:\d{2} BOOTSTRAP_PROCESS_ENTER",
            contents);
    }

    private string CreateLogPath()
    {
        return Path.Combine(_directory, "Diagnostics", "startup-bootstrap.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
