using GaltekClassroom.Agent.Session.Commands;
using System.Runtime.Versioning;

namespace GaltekClassroom.Agent.Session.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsUrlLauncherTests
{
    [Fact]
    public async Task LaunchAsync_BuildsShellOpenRequestWithoutParameters()
    {
        var executor = new RecordingShellExecutor(new WindowsShellExecuteResult(true, 0));
        var launcher = new WindowsUrlLauncher(executor);

        UrlLaunchResult result = await launcher.LaunchAsync(
            "https://example.test/activity",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        WindowsShellExecuteRequest request = Assert.Single(executor.Requests);
        Assert.Equal("open", request.Verb);
        Assert.Equal("https://example.test/activity", request.File);
        Assert.Null(request.Parameters);
    }

    [Fact]
    public async Task LaunchAsync_WhenShellRejects_ReturnsFailure()
    {
        var executor = new RecordingShellExecutor(new WindowsShellExecuteResult(false, 31));
        var launcher = new WindowsUrlLauncher(executor);

        UrlLaunchResult result = await launcher.LaunchAsync(
            "https://example.test/activity",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Single(executor.Requests);
    }

    [Fact]
    public async Task LaunchAsync_WhenUrlIsInvalid_DoesNotCallShell()
    {
        var executor = new RecordingShellExecutor(new WindowsShellExecuteResult(true, 0));
        var launcher = new WindowsUrlLauncher(executor);

        UrlLaunchResult result = await launcher.LaunchAsync(
            "javascript:alert(1)",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(executor.Requests);
    }

    private sealed class RecordingShellExecutor : IWindowsShellExecutor
    {
        private readonly WindowsShellExecuteResult _result;

        public RecordingShellExecutor(WindowsShellExecuteResult result)
        {
            _result = result;
        }

        public List<WindowsShellExecuteRequest> Requests { get; } = [];

        public WindowsShellExecuteResult Execute(WindowsShellExecuteRequest request)
        {
            Requests.Add(request);
            return _result;
        }
    }
}
