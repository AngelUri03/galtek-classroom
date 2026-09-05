using System.Buffers.Binary;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.CredentialProviderBridge;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class CredentialProviderBridgeTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ActivationStore_WhenNoActivation_ReturnsNone()
    {
        var store = new CredentialProviderActivationStore();

        Assert.Null(store.GetPending(FixedNow));
    }

    [Theory]
    [InlineData(ClassroomManagedWindowsAccountTypes.Primary)]
    [InlineData(ClassroomManagedWindowsAccountTypes.Secondary)]
    public void ActivationStore_AcceptsManagedAccountIds(string accountId)
    {
        var store = new CredentialProviderActivationStore();

        var result = store.SetPending(
            accountId,
            FixedNow,
            CredentialProviderActivationStore.DefaultTtl);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Activation);
        Assert.Equal(accountId, result.Activation.AccountId);
        Assert.Equal(FixedNow, result.Activation.CreatedAtUtc);
        Assert.Equal(FixedNow.Add(CredentialProviderActivationStore.DefaultTtl), result.Activation.ExpiresAtUtc);
    }

    [Fact]
    public void ActivationStore_RejectsUnspecifiedAccountId()
    {
        var store = new CredentialProviderActivationStore();

        var result = store.SetPending(
            "UNSPECIFIED",
            FixedNow,
            CredentialProviderActivationStore.DefaultTtl);

        Assert.False(result.Succeeded);
        Assert.Null(store.GetPending(FixedNow));
    }

    [Fact]
    public void ActivationStore_NewActivationReplacesPrevious()
    {
        var store = new CredentialProviderActivationStore();

        var first = store.SetPending(
            ClassroomManagedWindowsAccountTypes.Primary,
            FixedNow,
            TimeSpan.FromSeconds(30));
        var second = store.SetPending(
            ClassroomManagedWindowsAccountTypes.Secondary,
            FixedNow.AddSeconds(5),
            TimeSpan.FromSeconds(30));

        var pending = store.GetPending(FixedNow.AddSeconds(6));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.Activation!.ActivationId, pending!.ActivationId);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Secondary, pending.AccountId);
    }

    [Fact]
    public void ActivationStore_ExpiresLazily()
    {
        var store = new CredentialProviderActivationStore();
        store.SetPending(
            ClassroomManagedWindowsAccountTypes.Primary,
            FixedNow,
            TimeSpan.FromSeconds(30));

        Assert.Null(store.GetPending(FixedNow.AddSeconds(31)));
        Assert.Null(store.GetPending(FixedNow.AddSeconds(32)));
    }

    [Fact]
    public void ActivationStore_NewInstanceHasNoPersistedState()
    {
        var first = new CredentialProviderActivationStore();
        first.SetPending(
            ClassroomManagedWindowsAccountTypes.Primary,
            FixedNow,
            TimeSpan.FromSeconds(30));

        var restarted = new CredentialProviderActivationStore();

        Assert.Null(restarted.GetPending(FixedNow));
    }

    [Fact]
    public async Task RequestHandler_WhenNoActivation_ReturnsNone()
    {
        var handler = CreateHandler(new MutableClock(FixedNow));

        var response = await handler.HandleAsync(
            Request(CredentialProviderBridgeOperations.GetPendingActivationMetadata),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.Equal(CredentialProviderBridgeStatuses.Success, response.Status);
        Assert.Equal(CredentialProviderActivationStatuses.None, response.ActivationStatus);
        Assert.Null(response.PendingActivation);
    }

    [Fact]
    public async Task RequestHandler_ReturnsPendingPrimaryActivationMetadata()
    {
        var clock = new MutableClock(FixedNow);
        var store = new CredentialProviderActivationStore();
        store.SetPending(
            ClassroomManagedWindowsAccountTypes.Primary,
            FixedNow,
            TimeSpan.FromSeconds(30));
        var handler = new CredentialProviderBridgeRequestHandler(
            new CredentialProviderActivationService(store, clock));

        var response = await handler.HandleAsync(
            Request(CredentialProviderBridgeOperations.GetPendingActivationMetadata),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.Equal(CredentialProviderBridgeStatuses.Success, response.Status);
        Assert.Equal(CredentialProviderActivationStatuses.Pending, response.ActivationStatus);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Primary, response.PendingActivation!.AccountId);
    }

    [Fact]
    public async Task RequestHandler_ReturnsPendingSecondaryActivationMetadata()
    {
        var clock = new MutableClock(FixedNow);
        var store = new CredentialProviderActivationStore();
        store.SetPending(
            ClassroomManagedWindowsAccountTypes.Secondary,
            FixedNow,
            TimeSpan.FromSeconds(30));
        var handler = new CredentialProviderBridgeRequestHandler(
            new CredentialProviderActivationService(store, clock));

        var response = await handler.HandleAsync(
            Request(CredentialProviderBridgeOperations.GetPendingActivationMetadata),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.Equal(CredentialProviderActivationStatuses.Pending, response.ActivationStatus);
        Assert.Equal(ClassroomManagedWindowsAccountTypes.Secondary, response.PendingActivation!.AccountId);
    }

    [Fact]
    public async Task RequestHandler_PingReturnsSuccess()
    {
        var handler = CreateHandler(new MutableClock(FixedNow));

        var response = await handler.HandleAsync(
            Request(CredentialProviderBridgeOperations.Ping),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.Equal(CredentialProviderBridgeStatuses.Success, response.Status);
        Assert.Null(response.ActivationStatus);
        Assert.Null(response.PendingActivation);
    }

    [Fact]
    public async Task RequestHandler_RejectsUnsupportedProtocol()
    {
        var handler = CreateHandler(new MutableClock(FixedNow));

        var response = await handler.HandleAsync(
            Request(CredentialProviderBridgeOperations.Ping, protocolVersion: 2),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.Equal(CredentialProviderBridgeStatuses.Failed, response.Status);
        Assert.Equal(CredentialProviderBridgeErrorCodes.ProtocolUnsupported, response.ErrorCode);
    }

    [Fact]
    public async Task RequestHandler_RejectsProcessHintsInPayload()
    {
        var handler = CreateHandler(new MutableClock(FixedNow));
        var requestId = Guid.NewGuid().ToString("D");
        var json = $$"""
        {
          "protocolVersion": 1,
          "requestId": "{{requestId}}",
          "operation": "PING",
          "clientProcessId": 4,
          "windowsSid": "S-1-5-18",
          "username": "SYSTEM"
        }
        """;

        var response = await handler.HandleAsync(json, AuthorizedCaller(), CancellationToken.None);

        Assert.Equal(CredentialProviderBridgeStatuses.Failed, response.Status);
        Assert.Equal(CredentialProviderBridgeErrorCodes.MalformedRequest, response.ErrorCode);
    }

    [Fact]
    public async Task RequestHandler_PayloadCannotAuthorizeUnauthorizedCaller()
    {
        var handler = CreateHandler(new MutableClock(FixedNow));
        var requestId = Guid.NewGuid().ToString("D");
        var json = $$"""
        {
          "protocolVersion": 1,
          "requestId": "{{requestId}}",
          "operation": "PING",
          "windowsSid": "S-1-5-18"
        }
        """;

        var response = await handler.HandleAsync(
            json,
            CredentialProviderCallerValidation.Deny("fake caller"),
            CancellationToken.None);

        Assert.Equal(CredentialProviderBridgeStatuses.Failed, response.Status);
        Assert.Equal(CredentialProviderBridgeErrorCodes.Unauthorized, response.ErrorCode);
    }

    [Fact]
    public void CallerVerifier_WhenCallerIsNotSystem_Rejects()
    {
        using var pipe = CreateUnconnectedPipe();
        var verifier = CreateVerifier(clientSid: "S-1-5-32-544");

        var result = verifier.Verify(pipe);

        Assert.False(result.Authorized);
    }

    [Fact]
    public void CallerVerifier_WhenSystemProcessIsNotLogonUi_Rejects()
    {
        using var pipe = CreateUnconnectedPipe();
        var verifier = CreateVerifier(
            imagePath: Path.Combine(Path.GetTempPath(), "LogonUI.exe"));

        var result = verifier.Verify(pipe);

        Assert.False(result.Authorized);
    }

    [Fact]
    public void CallerVerifier_WhenProcessIdDoesNotMatchInspection_Rejects()
    {
        using var pipe = CreateUnconnectedPipe();
        var expectedPath = CredentialProviderCallerVerifier.DefaultExpectedLogonUiPath();
        var verifier = new CredentialProviderCallerVerifier(
            new FakePipeInspector(123, CredentialProviderBridgeProtocol.LocalSystemSid),
            new FakeProcessInspector(new CredentialProviderCallerProcessInfo(124, 1, expectedPath)),
            expectedPath);

        var result = verifier.Verify(pipe);

        Assert.False(result.Authorized);
    }

    [Fact]
    public void CallerVerifier_WhenExpectedLogonUiAndSystem_Accepts()
    {
        using var pipe = CreateUnconnectedPipe();
        var verifier = CreateVerifier();

        var result = verifier.Verify(pipe);

        Assert.True(result.Authorized);
        Assert.Equal(CredentialProviderBridgeProtocol.LocalSystemSid, result.ClientWindowsSid);
    }

    [Fact]
    public void BridgeContracts_DoNotContainPasswordFields()
    {
        var contractTypes = new[]
        {
            typeof(CredentialProviderBridgeRequest),
            typeof(CredentialProviderBridgeResponse),
            typeof(CredentialProviderActivationMetadata),
            typeof(CredentialProviderActivation)
        };

        foreach (var type in contractTypes)
        {
            var members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public);
            Assert.DoesNotContain(members, member =>
                member.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
                || member.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || member.Name.Contains("protectedData", StringComparison.OrdinalIgnoreCase)
                || member.Name.Contains("credentialId", StringComparison.OrdinalIgnoreCase)
                || member.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ResponseJson_DoesNotContainSecretFields()
    {
        var response = CredentialProviderBridgeResponse.Activation(
            Guid.NewGuid().ToString("D"),
            new CredentialProviderActivationMetadata
            {
                ActivationId = Guid.NewGuid().ToString("D"),
                AccountId = ClassroomManagedWindowsAccountTypes.Primary,
                CreatedAtUtc = FixedNow,
                ExpiresAtUtc = FixedNow.AddSeconds(30)
            });

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentialId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protectedData", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Framing_UsesBigEndianLengthPrefix()
    {
        var frame = CredentialProviderBridgeFraming.FrameJson("{}");

        Assert.Equal(6, frame.Length);
        Assert.Equal([0, 0, 0, 2], frame.Take(4).ToArray());
        Assert.Equal("{}", Encoding.UTF8.GetString(frame.Skip(4).ToArray()));
    }

    [Fact]
    public async Task Framing_ReadsExactPayload()
    {
        await using var stream = new MemoryStream();

        await CredentialProviderBridgeFraming.WriteJsonAsync(stream, """{"operation":"PING"}""", CancellationToken.None);
        stream.Position = 0;

        var json = await CredentialProviderBridgeFraming.ReadJsonAsync(stream, CancellationToken.None);

        Assert.Equal("""{"operation":"PING"}""", json);
    }

    [Fact]
    public async Task Framing_WhenLengthExceedsLimit_RejectsFrame()
    {
        var frame = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(frame, CredentialProviderBridgeProtocol.MaxMessageBytes + 1);
        await using var stream = new MemoryStream(frame);

        await Assert.ThrowsAsync<CredentialProviderBridgeFramingException>(
            () => CredentialProviderBridgeFraming.ReadJsonAsync(stream, CancellationToken.None));
    }

    [Fact]
    public void PipeSecurity_AllowsOnlyLocalSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var security = CredentialProviderBridgePipeStreamFactory.CreatePipeSecurity();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToArray();

        Assert.Single(rules);
        Assert.Equal(AccessControlType.Allow, rules[0].AccessControlType);
        Assert.Equal(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            rules[0].IdentityReference);
    }

    private static CredentialProviderBridgeRequestHandler CreateHandler(MutableClock clock)
    {
        return new CredentialProviderBridgeRequestHandler(
            new CredentialProviderActivationService(new CredentialProviderActivationStore(), clock));
    }

    private static string Request(
        string operation,
        int protocolVersion = CredentialProviderBridgeProtocol.ProtocolVersion)
    {
        return JsonSerializer.Serialize(
            new CredentialProviderBridgeRequest
            {
                ProtocolVersion = protocolVersion,
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = operation
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static CredentialProviderCallerValidation AuthorizedCaller()
    {
        return CredentialProviderCallerValidation.Allow(
            123,
            CredentialProviderCallerVerifier.DefaultExpectedLogonUiPath(),
            CredentialProviderBridgeProtocol.LocalSystemSid);
    }

    private static NamedPipeServerStream CreateUnconnectedPipe()
    {
        return new NamedPipeServerStream(
            "GaltekClassroom.CredentialProvider.Tests." + Guid.NewGuid().ToString("N"),
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private static CredentialProviderCallerVerifier CreateVerifier(
        string clientSid = CredentialProviderBridgeProtocol.LocalSystemSid,
        string? imagePath = null)
    {
        var expectedPath = CredentialProviderCallerVerifier.DefaultExpectedLogonUiPath();
        return new CredentialProviderCallerVerifier(
            new FakePipeInspector(123, clientSid),
            new FakeProcessInspector(new CredentialProviderCallerProcessInfo(
                123,
                1,
                imagePath ?? expectedPath)),
            expectedPath);
    }

    private sealed class FakePipeInspector : ICredentialProviderCallerPipeInspector
    {
        private readonly int _processId;
        private readonly string? _clientSid;

        public FakePipeInspector(int processId, string? clientSid)
        {
            _processId = processId;
            _clientSid = clientSid;
        }

        public bool IsWindows { get; init; } = true;

        public bool TryGetClientProcessId(PipeStream pipe, out int processId)
        {
            processId = _processId;
            return processId > 0;
        }

        public string? TryGetClientSid(PipeStream pipe)
        {
            return _clientSid;
        }
    }

    private sealed class FakeProcessInspector : ICredentialProviderCallerProcessInspector
    {
        private readonly CredentialProviderCallerProcessInfo? _processInfo;

        public FakeProcessInspector(CredentialProviderCallerProcessInfo? processInfo)
        {
            _processInfo = processInfo;
        }

        public CredentialProviderCallerProcessInfo? TryGetProcess(int processId)
        {
            return _processInfo;
        }
    }

    private sealed class MutableClock : ISystemClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }
}
