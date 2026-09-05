using System.Buffers.Binary;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.CredentialProviderBridge;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class CredentialProviderBridgeTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string PrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-1004";
    private const string SecondarySid = "S-1-5-21-1000000000-1000000000-1000000000-1005";

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
    public async Task ActivationStore_WaitForActivationChangeRegistersListenerAndWakesOnRemoteActivation()
    {
        var store = new CredentialProviderActivationStore();
        var observedGeneration = store.CurrentGeneration(FixedNow);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var waitTask = store.WaitForGenerationChangeAsync(
            observedGeneration,
            FixedNow,
            cancellation.Token);
        Assert.True(await store.WaitForListenerAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.Equal(1, store.ListenerCount);

        var activation = store.SetRemotePending(
            "operation-1",
            ClassroomManagedWindowsAccountTypes.Primary,
            FixedNow,
            TimeSpan.FromSeconds(30),
            autoSubmitRequested: true);

        Assert.True(activation.Succeeded);
        Assert.True(await waitTask > observedGeneration);
        Assert.Equal(0, store.ListenerCount);
    }

    [Fact]
    public async Task ActivationStore_WaitForListenerReturnsFalseWhenNoValidatedListenerArrives()
    {
        var store = new CredentialProviderActivationStore();

        var available = await store.WaitForListenerAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);

        Assert.False(available);
    }

    [Fact]
    public async Task ActivationStore_ReportBeforeWaitCompletionIsObserved()
    {
        var store = new CredentialProviderActivationStore();
        var activation = store.SetRemotePending(
            "operation-1",
            ClassroomManagedWindowsAccountTypes.Primary,
            FixedNow,
            TimeSpan.FromSeconds(30),
            autoSubmitRequested: true).Activation!;
        store.TrySnapshotIdentity(activation.ActivationId, PrimarySid, FixedNow, out _);
        store.TryConsume(activation.ActivationId, PrimarySid, FixedNow, out _);

        Assert.True(store.TryComplete(
            activation.ActivationId,
            CredentialProviderLogonCompletionOutcome.LocalSerializationFailed,
            FixedNow));

        var outcome = await store.WaitForCompletionAsync(
            "operation-1",
            activation.ActivationId,
            FixedNow,
            CancellationToken.None);

        Assert.Equal(CredentialProviderLogonCompletionOutcome.LocalSerializationFailed, outcome);
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

    [Theory]
    [InlineData(ClassroomManagedWindowsAccountTypes.Primary, PrimarySid, "AULA", "Primaria")]
    [InlineData(ClassroomManagedWindowsAccountTypes.Secondary, SecondarySid, "AULA", "Secundaria")]
    public async Task RequestHandler_ReturnsServiceDerivedActivationIdentity(
        string accountId,
        string windowsSid,
        string domain,
        string username)
    {
        await using var fixture = await BridgeFixture.CreateAsync(FixedNow);
        fixture.Resolver.Add(windowsSid, domain, username, WindowsAccountSidNameUse.User);
        await fixture.SaveBindingAsync(accountId, windowsSid, "WRONG\\Metadata");
        CredentialProviderActivationSetResult activation = fixture.Store.SetPending(accountId, FixedNow, TimeSpan.FromSeconds(30));
        var handler = fixture.CreateHandler();

        var result = await handler.HandleFrameAsync(
            Request(CredentialProviderBridgeOperations.GetPendingActivationIdentity),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.NotNull(result.JsonResponse);
        Assert.Equal(CredentialProviderBridgeStatuses.Success, result.JsonResponse!.Status);
        Assert.Equal(CredentialProviderActivationStatuses.Pending, result.JsonResponse.ActivationStatus);
        Assert.Equal(activation.Activation!.ActivationId, result.JsonResponse.PendingIdentity!.ActivationId);
        Assert.Equal(accountId, result.JsonResponse.PendingIdentity.AccountId);
        Assert.Equal(windowsSid, result.JsonResponse.PendingIdentity.UserSid);
        Assert.Equal(domain, result.JsonResponse.PendingIdentity.Domain);
        Assert.Equal(username, result.JsonResponse.PendingIdentity.Username);
        Assert.DoesNotContain("WRONG", result.JsonResponse.PendingIdentity.Domain, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestHandler_WhenBindingIsMissing_ReturnsNoIdentity()
    {
        await using var fixture = await BridgeFixture.CreateAsync(FixedNow);
        fixture.Store.SetPending(ClassroomManagedWindowsAccountTypes.Primary, FixedNow, TimeSpan.FromSeconds(30));
        var handler = fixture.CreateHandler();

        var result = await handler.HandleFrameAsync(
            Request(CredentialProviderBridgeOperations.GetPendingActivationIdentity),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.Equal(CredentialProviderActivationStatuses.None, result.JsonResponse!.ActivationStatus);
        Assert.Null(result.JsonResponse.PendingIdentity);
    }

    [Fact]
    public async Task RequestHandler_WhenSidIsNotUser_ReturnsNoIdentity()
    {
        await using var fixture = await BridgeFixture.CreateAsync(FixedNow);
        fixture.Resolver.Add(PrimarySid, "AULA", "Teachers", WindowsAccountSidNameUse.Group);
        await fixture.SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Primary, PrimarySid, "AULA\\Teachers");
        fixture.Store.SetPending(ClassroomManagedWindowsAccountTypes.Primary, FixedNow, TimeSpan.FromSeconds(30));
        var handler = fixture.CreateHandler();

        var result = await handler.HandleFrameAsync(
            Request(CredentialProviderBridgeOperations.GetPendingActivationIdentity),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.Equal(CredentialProviderActivationStatuses.None, result.JsonResponse!.ActivationStatus);
        Assert.Null(result.JsonResponse.PendingIdentity);
    }

    [Fact]
    public async Task ActivationStore_SnapshotsExpectedSidAfterIdentity()
    {
        await using var fixture = await BridgeFixture.CreateAsync(FixedNow);
        fixture.Resolver.Add(PrimarySid, "AULA", "Primaria", WindowsAccountSidNameUse.User);
        await fixture.SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Primary, PrimarySid, "AULA\\Primaria");
        fixture.Store.SetPending(ClassroomManagedWindowsAccountTypes.Primary, FixedNow, TimeSpan.FromSeconds(30));

        await fixture.CreateHandler().HandleFrameAsync(
            Request(CredentialProviderBridgeOperations.GetPendingActivationIdentity),
            AuthorizedCaller(),
            CancellationToken.None);

        var pending = fixture.Store.GetPending(FixedNow);
        Assert.Equal(CredentialProviderActivationState.IdentityResolved, pending!.State);
        Assert.Equal(PrimarySid, pending.ExpectedWindowsSid);
    }

    [Fact]
    public async Task AcquirePendingCredential_WhenBindingReboundAfterIdentity_ReturnsNoSecretWithoutDpapi()
    {
        await using var fixture = await BridgeFixture.CreateAsync(FixedNow);
        const string reboundSid = "S-1-5-21-1000000000-1000000000-1000000000-7777";
        fixture.Resolver.Add(PrimarySid, "AULA", "Primaria", WindowsAccountSidNameUse.User);
        fixture.Resolver.Add(reboundSid, "AULA", "Nueva", WindowsAccountSidNameUse.User);
        await fixture.SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Primary, PrimarySid, "AULA\\Primaria");
        var activation = fixture.Store.SetPending(ClassroomManagedWindowsAccountTypes.Primary, FixedNow, TimeSpan.FromSeconds(30));
        var handler = fixture.CreateHandler();
        await handler.HandleFrameAsync(
            Request(CredentialProviderBridgeOperations.GetPendingActivationIdentity),
            AuthorizedCaller(),
            CancellationToken.None);
        await fixture.ReplaceBindingAsync(ClassroomManagedWindowsAccountTypes.Primary, reboundSid, "AULA\\Nueva");

        using var result = await handler.HandleFrameAsync(
            Request(CredentialProviderBridgeOperations.AcquirePendingCredential, activation.Activation!.ActivationId),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.NotNull(result.BinaryPayload);
        Assert.True(CredentialProviderSecretResponse.TryParse(result.BinaryPayload, out var parsed));
        using (parsed)
        {
            Assert.False(parsed!.Succeeded);
            Assert.Empty(parsed.PasswordUtf16LittleEndian);
        }

        Assert.Equal(0, fixture.Protector.UnprotectCalls);
    }

    [Fact]
    public async Task AcquirePendingCredential_WhenValid_ReturnsBinarySecretOnceAndConsumesActivation()
    {
        await using var fixture = await BridgeFixture.CreateAsync(FixedNow);
        byte[] password = [0x41, 0x00, 0x00, 0xD8, 0x42, 0x00];
        fixture.Resolver.Add(PrimarySid, "AULA", "Primaria", WindowsAccountSidNameUse.User);
        await fixture.SaveBindingAsync(ClassroomManagedWindowsAccountTypes.Primary, PrimarySid, "AULA\\Primaria");
        await fixture.CredentialStore.ReplaceUtf16LittleEndianAsync(
            InstallationId,
            ClassroomManagedWindowsAccountTypes.Primary,
            password,
            CancellationToken.None);
        var activation = fixture.Store.SetPending(ClassroomManagedWindowsAccountTypes.Primary, FixedNow, TimeSpan.FromSeconds(30));
        var handler = fixture.CreateHandler();
        await handler.HandleFrameAsync(
            Request(CredentialProviderBridgeOperations.GetPendingActivationIdentity),
            AuthorizedCaller(),
            CancellationToken.None);

        using var first = await handler.HandleFrameAsync(
            Request(CredentialProviderBridgeOperations.AcquirePendingCredential, activation.Activation!.ActivationId),
            AuthorizedCaller(),
            CancellationToken.None);
        using var second = await handler.HandleFrameAsync(
            Request(CredentialProviderBridgeOperations.AcquirePendingCredential, activation.Activation!.ActivationId),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.NotNull(first.BinaryPayload);
        Assert.True(CredentialProviderSecretResponse.TryParse(first.BinaryPayload, out var parsedFirst));
        using (parsedFirst)
        {
            Assert.True(parsedFirst!.Succeeded);
            Assert.Equal(activation.Activation.ActivationId, parsedFirst.ActivationId);
            Assert.Equal(password, parsedFirst.PasswordUtf16LittleEndian);
        }

        Assert.True(CredentialProviderSecretResponse.TryParse(second.BinaryPayload!, out var parsedSecond));
        using (parsedSecond)
        {
            Assert.False(parsedSecond!.Succeeded);
        }

        Assert.Equal(1, fixture.Protector.UnprotectCalls);
        Assert.Null(fixture.Store.GetPending(FixedNow));
    }

    [Fact]
    public async Task RequestHandler_WaitForActivationChangeReturnsNewGeneration()
    {
        var clock = new MutableClock(FixedNow);
        var store = new CredentialProviderActivationStore();
        var service = new CredentialProviderActivationService(store, clock);
        var handler = new CredentialProviderBridgeRequestHandler(service);
        var observedGeneration = service.CurrentGeneration();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var waitTask = handler.HandleAsync(
            Request(
                CredentialProviderBridgeOperations.WaitForActivationChange,
                observedGeneration: observedGeneration),
            AuthorizedCaller(),
            cancellation.Token);
        Assert.True(await service.WaitForListenerAsync(TimeSpan.FromSeconds(1), CancellationToken.None));

        var activation = service.SetRemotePending(
            "operation-1",
            ClassroomManagedWindowsAccountTypes.Primary,
            TimeSpan.FromSeconds(30),
            autoSubmitRequested: true);

        Assert.True(activation.Succeeded);
        var response = await waitTask;

        Assert.Equal(CredentialProviderBridgeStatuses.Success, response.Status);
        Assert.True(response.Generation > observedGeneration);
    }

    [Theory]
    [InlineData(CredentialProviderLogonResultOutcomes.Success, CredentialProviderLogonCompletionOutcome.Success)]
    [InlineData(CredentialProviderLogonResultOutcomes.Failed, CredentialProviderLogonCompletionOutcome.Failed)]
    [InlineData(
        CredentialProviderLogonResultOutcomes.LocalSerializationFailed,
        CredentialProviderLogonCompletionOutcome.LocalSerializationFailed)]
    public async Task RequestHandler_ReportLogonResultCompletesActivation(
        string reportedOutcome,
        CredentialProviderLogonCompletionOutcome expectedOutcome)
    {
        var clock = new MutableClock(FixedNow);
        var store = new CredentialProviderActivationStore();
        var service = new CredentialProviderActivationService(store, clock);
        var handler = new CredentialProviderBridgeRequestHandler(service);
        var activation = service.SetRemotePending(
            "operation-1",
            ClassroomManagedWindowsAccountTypes.Primary,
            TimeSpan.FromSeconds(30),
            autoSubmitRequested: true).Activation!;
        store.TrySnapshotIdentity(activation.ActivationId, PrimarySid, FixedNow, out _);
        store.TryConsume(activation.ActivationId, PrimarySid, FixedNow, out _);

        var response = await handler.HandleAsync(
            Request(
                CredentialProviderBridgeOperations.ReportLogonResult,
                activation.ActivationId,
                outcome: reportedOutcome),
            AuthorizedCaller(),
            CancellationToken.None);
        var completed = await service.WaitForCompletionAsync(
            "operation-1",
            activation.ActivationId,
            CancellationToken.None);

        Assert.Equal(CredentialProviderBridgeStatuses.Success, response.Status);
        Assert.Equal(expectedOutcome, completed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TIMED_OUT")]
    [InlineData("SUCCESS_WITH_MESSAGE")]
    public async Task RequestHandler_ReportLogonResultRejectsUnsupportedOutcomes(string outcome)
    {
        var clock = new MutableClock(FixedNow);
        var store = new CredentialProviderActivationStore();
        var service = new CredentialProviderActivationService(store, clock);
        var handler = new CredentialProviderBridgeRequestHandler(service);
        var activation = service.SetRemotePending(
            "operation-1",
            ClassroomManagedWindowsAccountTypes.Primary,
            TimeSpan.FromSeconds(30),
            autoSubmitRequested: true).Activation!;

        var response = await handler.HandleAsync(
            Request(
                CredentialProviderBridgeOperations.ReportLogonResult,
                activation.ActivationId,
                outcome: outcome),
            AuthorizedCaller(),
            CancellationToken.None);

        Assert.Equal(CredentialProviderBridgeStatuses.Failed, response.Status);
        Assert.Equal(CredentialProviderBridgeErrorCodes.MalformedRequest, response.ErrorCode);
    }

    [Fact]
    public void SecretResponse_RejectsMalformedFrames()
    {
        var activationId = Guid.NewGuid().ToString("D");
        var valid = CredentialProviderSecretResponse.Success(activationId, [0x41, 0x00]);

        var trailing = new byte[valid.Length + 1];
        valid.CopyTo(trailing, 0);
        Assert.False(CredentialProviderSecretResponse.TryParse(trailing, out _));

        var oddLength = valid.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(oddLength.AsSpan(5 + 2 + 2 + 2 + activationId.Length, sizeof(int)), 1);
        Assert.False(CredentialProviderSecretResponse.TryParse(oddLength, out _));

        var unknownVersion = valid.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(unknownVersion.AsSpan(5, sizeof(ushort)), 99);
        Assert.False(CredentialProviderSecretResponse.TryParse(unknownVersion, out _));

        Assert.False(CredentialProviderSecretResponse.TryParse(valid.AsSpan(0, valid.Length - 1), out _));
    }

    [Fact]
    public void SecretResponse_DoesNotContainIdentityOrJsonPassword()
    {
        var payload = CredentialProviderSecretResponse.Success(Guid.NewGuid().ToString("D"), [0x41, 0x00]);
        var text = Encoding.UTF8.GetString(payload);

        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
        Assert.DoesNotContain("userSid", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accountReference", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vault", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentialId", text, StringComparison.OrdinalIgnoreCase);
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
        string? activationId = null,
        int protocolVersion = CredentialProviderBridgeProtocol.ProtocolVersion,
        long? observedGeneration = null,
        string? outcome = null)
    {
        return JsonSerializer.Serialize(
            new CredentialProviderBridgeRequest
            {
                ProtocolVersion = protocolVersion,
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = operation,
                ActivationId = activationId,
                ObservedGeneration = observedGeneration,
                Outcome = outcome
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

    private sealed class BridgeFixture : IAsyncDisposable
    {
        private readonly string _dataDirectory;
        private readonly MutableClock _clock;
        private readonly ManagedWindowsAccountBindingStore _bindingStore;

        private BridgeFixture(
            string dataDirectory,
            MutableClock clock,
            CredentialProviderActivationStore store,
            FakeWindowsAccountResolver resolver,
            FakeCredentialProtector protector,
            ManagedWindowsAccountBindingStore bindingStore,
            ManagedWindowsCredentialStore credentialStore)
        {
            _dataDirectory = dataDirectory;
            _clock = clock;
            Store = store;
            Resolver = resolver;
            Protector = protector;
            _bindingStore = bindingStore;
            CredentialStore = credentialStore;
        }

        public CredentialProviderActivationStore Store { get; }

        public FakeWindowsAccountResolver Resolver { get; }

        public FakeCredentialProtector Protector { get; }

        public ManagedWindowsCredentialStore CredentialStore { get; }

        public static async Task<BridgeFixture> CreateAsync(DateTimeOffset nowUtc)
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(),
                "GaltekClassroom.Agent.CredentialProviderBridge.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);

            var clock = new MutableClock(nowUtc);
            var identity = InstallationIdentity.Create(
                InstallationId,
                HardwareFingerprintFactory.FromRawValues(
                    ["CPU"],
                    ["BOARD"],
                    ["AA11BB22CC33"],
                    ["DISK"]),
                nowUtc);
            var identityStore = new InstallationIdentityStore(new InstallationIdentityStoreOptions(dataDirectory));
            await identityStore.WriteNewAsync(identity, CancellationToken.None);

            var bindingStore = new ManagedWindowsAccountBindingStore(
                new ManagedWindowsAccountBindingStoreOptions(dataDirectory),
                new NoOpManagedWindowsAccountBindingFileSecurity());
            var resolver = new FakeWindowsAccountResolver();
            var protector = new FakeCredentialProtector();
            var credentialStore = new ManagedWindowsCredentialStore(
                new ManagedWindowsCredentialStoreOptions(dataDirectory),
                bindingStore,
                resolver,
                protector,
                new NoOpManagedWindowsCredentialFileSecurity(),
                clock);

            return new BridgeFixture(
                dataDirectory,
                clock,
                new CredentialProviderActivationStore(),
                resolver,
                protector,
                bindingStore,
                credentialStore);
        }

        public CredentialProviderBridgeRequestHandler CreateHandler()
        {
            var identityStore = new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory));
            var service = new CredentialProviderActivationService(
                Store,
                _clock,
                identityStore,
                _bindingStore,
                Resolver,
                CredentialStore);

            return new CredentialProviderBridgeRequestHandler(service);
        }

        public async Task SaveBindingAsync(string accountId, string windowsSid, string accountReference)
        {
            var binding = ManagedWindowsAccountBinding.Create(accountId, windowsSid, accountReference, _clock.UtcNow);
            var result = await _bindingStore.AddAsync(InstallationId, binding, CancellationToken.None);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }

        public async Task ReplaceBindingAsync(string accountId, string windowsSid, string accountReference)
        {
            var binding = ManagedWindowsAccountBinding.Create(accountId, windowsSid, accountReference, _clock.UtcNow);
            var result = await _bindingStore.ReplaceAsync(InstallationId, binding, CancellationToken.None);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeWindowsAccountResolver : IWindowsAccountResolver
    {
        private readonly Dictionary<string, WindowsAccountIdentity> _bySid = new(StringComparer.OrdinalIgnoreCase);

        public void Add(
            string windowsSid,
            string domain,
            string username,
            WindowsAccountSidNameUse use)
        {
            _bySid[windowsSid] = new WindowsAccountIdentity(
                windowsSid,
                $"{domain}\\{username}",
                use)
            {
                Domain = domain,
                Username = username
            };
        }

        public WindowsAccountResolution ResolveCurrentUser()
        {
            throw new NotSupportedException();
        }

        public WindowsAccountResolution ResolveAccount(string accountName)
        {
            throw new NotSupportedException();
        }

        public WindowsAccountResolution ResolveSid(string windowsSid)
        {
            return _bySid.TryGetValue(windowsSid, out var identity)
                ? WindowsAccountResolution.Resolved(identity)
                : WindowsAccountResolution.NotFound("not found");
        }
    }

    private sealed class FakeCredentialProtector : IManagedWindowsCredentialProtector
    {
        public int UnprotectCalls { get; private set; }

        public ManagedWindowsCredentialProtectionResult Protect(
            byte[] plaintext,
            byte[] optionalEntropy)
        {
            return ManagedWindowsCredentialProtectionResult.Protected(Transform(plaintext));
        }

        public ManagedWindowsCredentialProtectionResult Unprotect(
            byte[] protectedData,
            byte[] optionalEntropy)
        {
            UnprotectCalls++;
            return ManagedWindowsCredentialProtectionResult.Unprotected(Transform(protectedData));
        }

        private static byte[] Transform(byte[] data)
        {
            var copy = data.ToArray();
            for (var index = 0; index < copy.Length; index++)
            {
                copy[index] ^= 0xA5;
            }

            return copy;
        }
    }
}
