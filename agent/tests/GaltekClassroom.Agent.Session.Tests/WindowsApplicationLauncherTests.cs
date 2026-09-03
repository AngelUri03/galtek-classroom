using GaltekClassroom.Agent.Session.Commands;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class WindowsApplicationLauncherTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.WindowsApplicationLauncher.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LaunchAsync_WhenExecutableIsValid_CallsCreateProcessWithExactApplicationName()
    {
        var executablePath = CreateDummyExe("SchoolApp.exe");
        var creator = RecordingProcessCreator.Success();
        var closer = new RecordingHandleCloser();
        var launcher = new WindowsApplicationLauncher(creator, closer);

        var result = await launcher.LaunchAsync(executablePath, CancellationToken.None);

        Assert.True(result.Succeeded);
        var request = Assert.Single(creator.Requests);
        Assert.Equal(Path.GetFullPath(executablePath), request.ApplicationName);
        Assert.Null(request.CommandLine);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(executablePath)), request.WorkingDirectory);
        Assert.False(request.InheritHandles);
    }

    [Fact]
    public async Task LaunchAsync_WhenCreateProcessFails_ReturnsFailure()
    {
        var executablePath = CreateDummyExe("SchoolApp.exe");
        var launcher = new WindowsApplicationLauncher(
            RecordingProcessCreator.Failure(),
            new RecordingHandleCloser());

        var result = await launcher.LaunchAsync(executablePath, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task LaunchAsync_WhenCreateProcessSucceeds_ClosesProcessAndThreadHandles()
    {
        var executablePath = CreateDummyExe("SchoolApp.exe");
        var closer = new RecordingHandleCloser();
        var launcher = new WindowsApplicationLauncher(RecordingProcessCreator.Success(), closer);

        await launcher.LaunchAsync(executablePath, CancellationToken.None);

        Assert.Equal([new IntPtr(100), new IntPtr(200)], closer.ClosedHandles);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private string CreateDummyExe(string fileName)
    {
        Directory.CreateDirectory(_dataDirectory);
        var path = Path.Combine(_dataDirectory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private sealed class RecordingProcessCreator : IWindowsProcessCreator
    {
        private readonly WindowsCreateProcessResult _result;

        private RecordingProcessCreator(WindowsCreateProcessResult result)
        {
            _result = result;
        }

        public List<WindowsCreateProcessRequest> Requests { get; } = [];

        public static RecordingProcessCreator Success()
        {
            return new RecordingProcessCreator(new WindowsCreateProcessResult(
                true,
                new IntPtr(100),
                new IntPtr(200),
                0));
        }

        public static RecordingProcessCreator Failure()
        {
            return new RecordingProcessCreator(new WindowsCreateProcessResult(
                false,
                IntPtr.Zero,
                IntPtr.Zero,
                5));
        }

        public WindowsCreateProcessResult CreateProcess(WindowsCreateProcessRequest request)
        {
            Requests.Add(request);
            return _result;
        }
    }

    private sealed class RecordingHandleCloser : IWindowsHandleCloser
    {
        public List<IntPtr> ClosedHandles { get; } = [];

        public void Close(IntPtr handle)
        {
            ClosedHandles.Add(handle);
        }
    }
}
