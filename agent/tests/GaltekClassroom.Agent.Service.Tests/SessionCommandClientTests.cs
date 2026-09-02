using System.IO.Pipes;
using System.Text.Json;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class SessionCommandClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task PingAsync_WhenNoInteractiveSession_ReturnsUnavailable()
    {
        var verifier = new RecordingServerVerifier(verified: true);
        var client = CreateClient(
            InteractiveSessionResolution.Unavailable(SessionCommandErrorCodes.SessionAgentUnavailable),
            verifier);

        var result = await client.PingAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(SessionCommandErrorCodes.SessionAgentUnavailable, result.ErrorCode);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task PingAsync_WhenSessionIsZero_NeverConnects()
    {
        var verifier = new RecordingServerVerifier(verified: true);
        var client = CreateClient(
            InteractiveSessionResolution.Success(0),
            verifier);

        var result = await client.PingAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(SessionCommandErrorCodes.SessionAgentUnavailable, result.ErrorCode);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task PingAsync_WhenPipeDoesNotExist_ReturnsStructuredFailure()
    {
        var client = CreateClient(
            InteractiveSessionResolution.Success(Random.Shared.Next(220_001, 270_000)),
            new RecordingServerVerifier(verified: true));

        var result = await client.PingAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ErrorCode, new[]
        {
            SessionCommandErrorCodes.SessionAgentUnavailable,
            SessionCommandErrorCodes.SessionChannelTimeout
        });
    }

    [Fact]
    public async Task PingAsync_WhenServerVerifierRejects_DoesNotSendCommand()
    {
        var sessionId = Random.Shared.Next(270_001, 320_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = AcceptAndObserveRequestAsync(
            sessionId,
            cancellation.Token);
        var verifier = new RecordingServerVerifier(verified: false);
        var client = CreateClient(
            InteractiveSessionResolution.Success(sessionId),
            verifier);

        var result = await client.PingAsync(cancellation.Token);
        var observedRequest = await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(SessionCommandErrorCodes.SessionChannelUnauthorized, result.ErrorCode);
        Assert.Equal(1, verifier.Calls);
        Assert.False(observedRequest);
    }

    [Fact]
    public async Task PingAsync_WhenServerIsValid_ReturnsSuccess()
    {
        var sessionId = Random.Shared.Next(320_001, 370_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = EchoSuccessAsync(sessionId, cancellation.Token);
        var client = CreateClient(
            InteractiveSessionResolution.Success(sessionId),
            new RecordingServerVerifier(verified: true));

        var result = await client.PingAsync(cancellation.Token);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_WhenResponseRequestIdDiffers_ReturnsInvalidResponse()
    {
        var sessionId = Random.Shared.Next(370_001, 420_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = RespondWithWrongRequestIdAsync(sessionId, cancellation.Token);
        var client = CreateClient(
            InteractiveSessionResolution.Success(sessionId),
            new RecordingServerVerifier(verified: true));

        var result = await client.SendAsync(new SessionCommandRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            CommandType = SessionCommandTypes.ChannelPing
        }, cancellation.Token);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(SessionCommandErrorCodes.SessionChannelInvalidResponse, result.ErrorCode);
    }

    [Fact]
    public async Task PingAsync_WhenResponseTimesOut_ReturnsTimeout()
    {
        var sessionId = Random.Shared.Next(420_001, 470_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = AcceptWithoutRespondingAsync(sessionId, cancellation.Token);
        var client = CreateClient(
            InteractiveSessionResolution.Success(sessionId),
            new RecordingServerVerifier(verified: true));

        var result = await client.PingAsync(cancellation.Token);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(SessionCommandErrorCodes.SessionChannelTimeout, result.ErrorCode);
    }

    [Fact]
    public async Task PingAsync_DoesNotFallbackToLocalIpcV1()
    {
        var sessionId = Random.Shared.Next(470_001, 520_000);
        await using var localIpcServer = new NamedPipeServerStream(
            LocalIpcProtocol.PipeName + "." + Guid.NewGuid().ToString("N"),
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var client = CreateClient(
            InteractiveSessionResolution.Success(sessionId),
            new RecordingServerVerifier(verified: true));

        var result = await client.PingAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ErrorCode, new[]
        {
            SessionCommandErrorCodes.SessionAgentUnavailable,
            SessionCommandErrorCodes.SessionChannelTimeout
        });
    }

    private static SessionCommandClient CreateClient(
        InteractiveSessionResolution resolution,
        ISessionAgentServerVerifier verifier)
    {
        return new SessionCommandClient(
            new StaticInteractiveSessionResolver(resolution),
            verifier,
            new SessionCommandClientOptions(TestTimeout));
    }

    private static async Task EchoSuccessAsync(int sessionId, CancellationToken cancellationToken)
    {
        await using var server = CreateServer(sessionId);
        await server.WaitForConnectionAsync(cancellationToken);
        var request = await ReadRequestAsync(server, cancellationToken);
        await WriteResponseAsync(server, SessionCommandResponse.Success(request.RequestId), cancellationToken);
    }

    private static async Task RespondWithWrongRequestIdAsync(
        int sessionId,
        CancellationToken cancellationToken)
    {
        await using var server = CreateServer(sessionId);
        await server.WaitForConnectionAsync(cancellationToken);
        _ = await ReadRequestAsync(server, cancellationToken);
        await WriteResponseAsync(
            server,
            SessionCommandResponse.Success(Guid.NewGuid().ToString("D")),
            cancellationToken);
    }

    private static async Task AcceptWithoutRespondingAsync(
        int sessionId,
        CancellationToken cancellationToken)
    {
        await using var server = CreateServer(sessionId);
        await server.WaitForConnectionAsync(cancellationToken);
        _ = await ReadRequestAsync(server, cancellationToken);
        await Task.Delay(TestTimeout + TestTimeout, cancellationToken);
    }

    private static async Task<bool> AcceptAndObserveRequestAsync(
        int sessionId,
        CancellationToken cancellationToken)
    {
        await using var server = CreateServer(sessionId);
        await server.WaitForConnectionAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TestTimeout + TestTimeout);
            _ = await SessionCommandFraming.ReadJsonAsync(server, timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static NamedPipeServerStream CreateServer(int sessionId)
    {
        return new NamedPipeServerStream(
            SessionCommandProtocol.PipeNameForSession(sessionId),
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private static async Task<SessionCommandRequest> ReadRequestAsync(
        PipeStream stream,
        CancellationToken cancellationToken)
    {
        var requestJson = await SessionCommandFraming.ReadJsonAsync(stream, cancellationToken);
        return JsonSerializer.Deserialize<SessionCommandRequest>(requestJson, JsonOptions)!;
    }

    private static async Task WriteResponseAsync(
        PipeStream stream,
        SessionCommandResponse response,
        CancellationToken cancellationToken)
    {
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await SessionCommandFraming.WriteJsonAsync(stream, responseJson, cancellationToken);
    }

    private sealed class StaticInteractiveSessionResolver : IInteractiveSessionResolver
    {
        private readonly InteractiveSessionResolution _resolution;

        public StaticInteractiveSessionResolver(InteractiveSessionResolution resolution)
        {
            _resolution = resolution;
        }

        public InteractiveSessionResolution Resolve()
        {
            return _resolution;
        }
    }

    private sealed class RecordingServerVerifier : ISessionAgentServerVerifier
    {
        private readonly bool _verified;

        public RecordingServerVerifier(bool verified)
        {
            _verified = verified;
        }

        public int Calls { get; private set; }

        public bool Verify(NamedPipeClientStream pipe, int expectedSessionId)
        {
            ArgumentNullException.ThrowIfNull(pipe);
            Calls++;
            return _verified;
        }
    }
}
