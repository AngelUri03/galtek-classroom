using System.IO.Pipes;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class SessionAgentServerVerifierTests
{
    [Fact]
    public async Task Verify_WhenServerProcessIsInAnotherSession_Rejects()
    {
        var expectedSessionId = 42;
        var expectedPath = ExpectedPath();
        var verifier = new WindowsSessionAgentServerVerifier(
            new StaticProcessInspector(new SessionAgentProcessInfo(
                ProcessId: 123,
                SessionId: expectedSessionId + 1,
                ExecutablePath: expectedPath)),
            expectedPath);

        await WithConnectedPipeAsync(expectedSessionId, pipe =>
        {
            Assert.False(verifier.Verify(pipe, expectedSessionId));
        });
    }

    [Fact]
    public async Task Verify_WhenServerExecutablePathDiffers_Rejects()
    {
        var expectedSessionId = 43;
        var expectedPath = ExpectedPath();
        var verifier = new WindowsSessionAgentServerVerifier(
            new StaticProcessInspector(new SessionAgentProcessInfo(
                ProcessId: 123,
                SessionId: expectedSessionId,
                ExecutablePath: Path.Combine(
                    Path.GetTempPath(),
                    ProductInfo.SessionAgentExecutableName))),
            expectedPath);

        await WithConnectedPipeAsync(expectedSessionId, pipe =>
        {
            Assert.False(verifier.Verify(pipe, expectedSessionId));
        });
    }

    [Fact]
    public async Task Verify_WhenServerSessionAndExecutableMatch_Accepts()
    {
        var expectedSessionId = 44;
        var expectedPath = ExpectedPath();
        var verifier = new WindowsSessionAgentServerVerifier(
            new StaticProcessInspector(new SessionAgentProcessInfo(
                ProcessId: 123,
                SessionId: expectedSessionId,
                ExecutablePath: expectedPath)),
            expectedPath);

        await WithConnectedPipeAsync(expectedSessionId, pipe =>
        {
            Assert.True(verifier.Verify(pipe, expectedSessionId));
        });
    }

    private static async Task WithConnectedPipeAsync(
        int sessionId,
        Action<NamedPipeClientStream> assertion)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pipeName = SessionCommandProtocol.PipeNameForSession(
            Random.Shared.Next(520_001, 570_000) + sessionId);
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        var waitTask = server.WaitForConnectionAsync(cancellation.Token);
        await client.ConnectAsync(5_000, cancellation.Token);
        await waitTask;

        assertion(client);
    }

    private static string ExpectedPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "ProgramFiles",
            "Galtek",
            "Classroom",
            "Agent",
            "Session",
            ProductInfo.SessionAgentExecutableName);
    }

    private sealed class StaticProcessInspector : ISessionAgentProcessInspector
    {
        private readonly SessionAgentProcessInfo? _processInfo;

        public StaticProcessInspector(SessionAgentProcessInfo? processInfo)
        {
            _processInfo = processInfo;
        }

        public SessionAgentProcessInfo? TryGetProcess(int processId)
        {
            return _processInfo is null
                ? null
                : _processInfo with { ProcessId = processId };
        }
    }
}
