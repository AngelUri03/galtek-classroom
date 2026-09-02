using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GaltekClassroom.Agent.Session.Commands;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class SessionCommandServerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Handle_WhenCallerSendsChannelPing_ReturnsSuccess()
    {
        var requestId = Guid.NewGuid().ToString("D");

        var response = SessionCommandServer.Handle(new SessionCommandRequest
        {
            RequestId = requestId,
            CommandType = SessionCommandTypes.ChannelPing
        });

        Assert.Equal(SessionCommandStatuses.Success, response.Status);
        Assert.Null(response.ErrorCode);
        Assert.Equal(requestId, response.RequestId);
    }

    [Fact]
    public void Handle_WhenProtocolVersionIsWrong_RejectsRequest()
    {
        var requestId = Guid.NewGuid().ToString("D");

        var response = SessionCommandServer.Handle(new SessionCommandRequest
        {
            ProtocolVersion = 99,
            RequestId = requestId,
            CommandType = SessionCommandTypes.ChannelPing
        });

        Assert.Equal(SessionCommandStatuses.Failed, response.Status);
        Assert.Equal(SessionCommandErrorCodes.SessionChannelProtocolMismatch, response.ErrorCode);
        Assert.Equal(requestId, response.RequestId);
    }

    [Fact]
    public void Handle_WhenRequestIdIsMissing_RejectsRequest()
    {
        var response = SessionCommandServer.Handle(new SessionCommandRequest
        {
            RequestId = string.Empty,
            CommandType = SessionCommandTypes.ChannelPing
        });

        Assert.Equal(SessionCommandErrorCodes.SessionChannelMalformedRequest, response.ErrorCode);
    }

    [Fact]
    public void Handle_WhenCommandIsUnknown_RejectsRequest()
    {
        var response = SessionCommandServer.Handle(new SessionCommandRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            CommandType = "RUN_PROCESS"
        });

        Assert.Equal(SessionCommandErrorCodes.SessionCommandNotSupported, response.ErrorCode);
    }

    [Fact]
    public void Handle_ChannelPingStillWorks()
    {
        Assert.Equal(SessionCommandStatuses.Success, SessionCommandServer.Handle(new SessionCommandRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            CommandType = SessionCommandTypes.ChannelPing
        }).Status);

        Assert.Equal(SessionCommandErrorCodes.SessionCommandNotSupported, SessionCommandServer.Handle(new SessionCommandRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            CommandType = "RUN_PROCESS"
        }).ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_WhenOpenUrlIsValid_CallsLauncherExactlyOnce()
    {
        var launcher = RecordingUrlLauncher.Success();
        var request = CreateOpenUrlRequest("https://example.test/activity");

        SessionCommandResponse response = await SessionCommandServer.HandleAsync(
            request,
            launcher,
            CancellationToken.None);

        Assert.Equal(SessionCommandStatuses.Success, response.Status);
        Assert.Null(response.ErrorCode);
        Assert.Equal(new[] { "https://example.test/activity" }, launcher.Urls);
    }

    [Fact]
    public async Task HandleAsync_WhenOpenUrlIsInvalid_DoesNotCallLauncher()
    {
        var launcher = RecordingUrlLauncher.Success();
        var request = CreateOpenUrlRequest("javascript:alert(1)");

        SessionCommandResponse response = await SessionCommandServer.HandleAsync(
            request,
            launcher,
            CancellationToken.None);

        Assert.Equal(SessionCommandStatuses.Failed, response.Status);
        Assert.Equal(SessionCommandErrorCodes.InvalidUrl, response.ErrorCode);
        Assert.Empty(launcher.Urls);
    }

    [Fact]
    public async Task HandleAsync_WhenProtocolVersionIsWrong_DoesNotCallLauncher()
    {
        var launcher = RecordingUrlLauncher.Success();
        var request = CreateOpenUrlRequest("https://example.test/activity") with
        {
            ProtocolVersion = 99
        };

        SessionCommandResponse response = await SessionCommandServer.HandleAsync(
            request,
            launcher,
            CancellationToken.None);

        Assert.Equal(SessionCommandErrorCodes.SessionChannelProtocolMismatch, response.ErrorCode);
        Assert.Empty(launcher.Urls);
    }

    [Fact]
    public async Task HandleAsync_WhenCommandIsUnknown_DoesNotCallLauncher()
    {
        var launcher = RecordingUrlLauncher.Success();

        SessionCommandResponse response = await SessionCommandServer.HandleAsync(
            new SessionCommandRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                CommandType = "RUN_PROCESS"
            },
            launcher,
            CancellationToken.None);

        Assert.Equal(SessionCommandErrorCodes.SessionCommandNotSupported, response.ErrorCode);
        Assert.Empty(launcher.Urls);
    }

    [Fact]
    public async Task HandleAsync_WhenLauncherFails_ReturnsStructuredFailure()
    {
        var launcher = RecordingUrlLauncher.Failed();

        SessionCommandResponse response = await SessionCommandServer.HandleAsync(
            CreateOpenUrlRequest("https://example.test/activity"),
            launcher,
            CancellationToken.None);

        Assert.Equal(SessionCommandStatuses.Failed, response.Status);
        Assert.Equal(SessionCommandErrorCodes.UrlLaunchFailed, response.ErrorCode);
        Assert.Single(launcher.Urls);
    }

    [Fact]
    public async Task RunAsync_WhenCallerIsUnauthorized_RejectsConnection()
    {
        var sessionId = Random.Shared.Next(20_000, 70_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new SessionCommandServer(
            new TestPipeStreamFactory(),
            new StaticCallerVerifier(authorized: false));
        var serverTask = server.RunAsync(sessionId, cancellation.Token);

        await using var client = await ConnectAsync(sessionId, cancellation.Token);
        var responseJson = await SessionCommandFraming.ReadJsonAsync(client, cancellation.Token);
        var response = JsonSerializer.Deserialize<SessionCommandResponse>(responseJson, JsonOptions)!;

        cancellation.Cancel();
        await IgnoreCancellationAsync(serverTask);

        Assert.Equal(SessionCommandErrorCodes.SessionChannelUnauthorized, response.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_WhenCallerIsUnauthorized_DoesNotCallLauncher()
    {
        var sessionId = Random.Shared.Next(220_001, 270_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var launcher = RecordingUrlLauncher.Success();
        var server = new SessionCommandServer(
            new TestPipeStreamFactory(),
            new StaticCallerVerifier(authorized: false),
            launcher);
        var serverTask = server.RunAsync(sessionId, cancellation.Token);

        await using var client = await ConnectAsync(sessionId, cancellation.Token);
        var responseJson = await SessionCommandFraming.ReadJsonAsync(client, cancellation.Token);
        var response = JsonSerializer.Deserialize<SessionCommandResponse>(responseJson, JsonOptions)!;

        cancellation.Cancel();
        await IgnoreCancellationAsync(serverTask);

        Assert.Equal(SessionCommandErrorCodes.SessionChannelUnauthorized, response.ErrorCode);
        Assert.Empty(launcher.Urls);
    }

    [Fact]
    public async Task RunAsync_WhenCallerIsAuthorized_ChannelPingCanSucceed()
    {
        var sessionId = Random.Shared.Next(70_001, 120_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new SessionCommandServer(
            new TestPipeStreamFactory(),
            new StaticCallerVerifier(authorized: true));
        var serverTask = server.RunAsync(sessionId, cancellation.Token);
        var requestId = Guid.NewGuid().ToString("D");

        var response = await SendRequestAsync(sessionId, new SessionCommandRequest
        {
            RequestId = requestId,
            CommandType = SessionCommandTypes.ChannelPing
        }, cancellation.Token);

        cancellation.Cancel();
        await IgnoreCancellationAsync(serverTask);

        Assert.Equal(SessionCommandStatuses.Success, response.Status);
        Assert.Equal(requestId, response.RequestId);
        Assert.Null(response.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_WhenMalformedRequestArrives_RejectsSafely()
    {
        var sessionId = Random.Shared.Next(120_001, 170_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new SessionCommandServer(
            new TestPipeStreamFactory(),
            new StaticCallerVerifier(authorized: true));
        var serverTask = server.RunAsync(sessionId, cancellation.Token);

        await using var client = await ConnectAsync(sessionId, cancellation.Token);
        await SessionCommandFraming.WriteJsonAsync(client, "{ not-json", cancellation.Token);
        var responseJson = await SessionCommandFraming.ReadJsonAsync(client, cancellation.Token);
        var response = JsonSerializer.Deserialize<SessionCommandResponse>(responseJson, JsonOptions)!;

        cancellation.Cancel();
        await IgnoreCancellationAsync(serverTask);

        Assert.Equal(SessionCommandErrorCodes.SessionChannelMalformedRequest, response.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_StopsCleanly()
    {
        var sessionId = Random.Shared.Next(170_001, 220_000);
        using var cancellation = new CancellationTokenSource();
        var server = new SessionCommandServer(
            new TestPipeStreamFactory(),
            new StaticCallerVerifier(authorized: true));

        var serverTask = server.RunAsync(sessionId, cancellation.Token);
        cancellation.Cancel();

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ProductivePipeSecurity_AllowsOnlyLocalSystemClientAndDoesNotUseCurrentUserOnly()
    {
        var options = SessionCommandPipeStreamFactory.ProductivePipeOptions;
        var security = SessionCommandPipeStreamFactory.CreatePipeSecurity();
        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            targetType: typeof(SecurityIdentifier))
            .OfType<PipeAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .ToArray();
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var builtinUsers = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        Assert.Equal(PipeOptions.Asynchronous, options);
        Assert.False((options & PipeOptions.CurrentUserOnly) == PipeOptions.CurrentUserOnly);
        var localSystemRule = Assert.Single(
            rules,
            rule => localSystem.Equals(rule.IdentityReference));
        Assert.True((localSystemRule.PipeAccessRights & PipeAccessRights.ReadWrite) == PipeAccessRights.ReadWrite);
        Assert.False((localSystemRule.PipeAccessRights & PipeAccessRights.FullControl) == PipeAccessRights.FullControl);
        Assert.DoesNotContain(rules, rule => everyone.Equals(rule.IdentityReference));
        Assert.DoesNotContain(rules, rule => authenticatedUsers.Equals(rule.IdentityReference));
        Assert.DoesNotContain(rules, rule => builtinUsers.Equals(rule.IdentityReference));
        Assert.All(rules, rule => Assert.Equal(localSystem, rule.IdentityReference));
    }

    private static async Task<SessionCommandResponse> SendRequestAsync(
        int sessionId,
        SessionCommandRequest request,
        CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(sessionId, cancellationToken);
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        await SessionCommandFraming.WriteJsonAsync(client, requestJson, cancellationToken);
        var responseJson = await SessionCommandFraming.ReadJsonAsync(client, cancellationToken);
        return JsonSerializer.Deserialize<SessionCommandResponse>(responseJson, JsonOptions)!;
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(
        int sessionId,
        CancellationToken cancellationToken)
    {
        var client = new NamedPipeClientStream(
            ".",
            SessionCommandProtocol.PipeNameForSession(sessionId),
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(5_000, cancellationToken);
        return client;
    }

    private static SessionCommandRequest CreateOpenUrlRequest(string url)
    {
        return new SessionCommandRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            CommandType = SessionCommandTypes.OpenUrl,
            OpenUrl = new SessionOpenUrlCommand
            {
                OperationId = Guid.NewGuid().ToString("D"),
                Url = url
            }
        };
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class TestPipeStreamFactory : ISessionCommandPipeStreamFactory
    {
        public NamedPipeServerStream CreateServerStream(string pipeName)
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }
    }

    private sealed class StaticCallerVerifier : ISessionCommandCallerVerifier
    {
        private readonly bool _authorized;

        public StaticCallerVerifier(bool authorized)
        {
            _authorized = authorized;
        }

        public bool IsAuthorized(PipeStream pipe)
        {
            ArgumentNullException.ThrowIfNull(pipe);
            return _authorized;
        }
    }

    private sealed class RecordingUrlLauncher : IUrlLauncher
    {
        private readonly UrlLaunchResult _result;

        private RecordingUrlLauncher(UrlLaunchResult result)
        {
            _result = result;
        }

        public List<string> Urls { get; } = [];

        public static RecordingUrlLauncher Success()
        {
            return new RecordingUrlLauncher(UrlLaunchResult.Success());
        }

        public static RecordingUrlLauncher Failed()
        {
            return new RecordingUrlLauncher(UrlLaunchResult.Failed("Launch failed."));
        }

        public Task<UrlLaunchResult> LaunchAsync(string url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Urls.Add(url);
            return Task.FromResult(_result);
        }
    }
}
