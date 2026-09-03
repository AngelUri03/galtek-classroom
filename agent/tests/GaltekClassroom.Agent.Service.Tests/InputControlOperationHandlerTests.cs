using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.InputControl;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class InputControlOperationHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LockInputHandler_SendsOnlyTypedSessionCommand()
    {
        var client = RecordingSessionCommandClient.Success();
        var handler = new LockInputOperationHandler(client, NullLogger<LockInputOperationHandler>.Instance);

        var result = await handler.HandleAsync(CreateRequest(NetworkOperationType.LockInput), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(1, client.LockCalls);
        Assert.Equal(0, client.UnlockCalls);
        Assert.Empty(client.OpenUrlCalls);
        Assert.Empty(client.OpenApplicationCalls);
    }

    [Fact]
    public async Task UnlockInputHandler_SendsOnlyTypedSessionCommand()
    {
        var client = RecordingSessionCommandClient.Success();
        var handler = new UnlockInputOperationHandler(client, NullLogger<UnlockInputOperationHandler>.Instance);

        var result = await handler.HandleAsync(CreateRequest(NetworkOperationType.UnlockInput), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(1, client.UnlockCalls);
        Assert.Equal(0, client.LockCalls);
        Assert.Empty(client.OpenUrlCalls);
        Assert.Empty(client.OpenApplicationCalls);
    }

    [Theory]
    [InlineData(SessionCommandErrorCodes.InputLockFailed, NetworkOperationErrorCode.InputLockFailed)]
    [InlineData(SessionCommandErrorCodes.InputUnlockFailed, NetworkOperationErrorCode.InputUnlockFailed)]
    [InlineData(SessionCommandErrorCodes.SessionAgentUnavailable, NetworkOperationErrorCode.SessionAgentUnavailable)]
    [InlineData(SessionCommandErrorCodes.SessionCommandResultUnknown, NetworkOperationErrorCode.SessionCommandResultUnknown)]
    public async Task Handlers_PreserveStructuredSessionErrors(
        string sessionErrorCode,
        NetworkOperationErrorCode expectedErrorCode)
    {
        var client = RecordingSessionCommandClient.Failure(sessionErrorCode);
        var lockResult = await new LockInputOperationHandler(
                client,
                NullLogger<LockInputOperationHandler>.Instance)
            .HandleAsync(CreateRequest(NetworkOperationType.LockInput), CancellationToken.None);

        Assert.Equal(expectedErrorCode, lockResult.ErrorCode);
    }

    [Fact]
    public async Task LockInput_DoesNotAcceptOperationParameters()
    {
        var client = RecordingSessionCommandClient.Success();
        var request = CreateRequest(NetworkOperationType.LockInput);
        request.OpenUrl = new OpenUrlOperationParameters { Url = "https://example.test" };

        var result = await new LockInputOperationHandler(
                client,
                NullLogger<LockInputOperationHandler>.Instance)
            .HandleAsync(request, CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.ProtocolViolation, result.ErrorCode);
        Assert.Equal(0, client.LockCalls);
    }

    [Fact]
    public async Task UnlockInput_DoesNotAcceptOperationParameters()
    {
        var client = RecordingSessionCommandClient.Success();
        var request = CreateRequest(NetworkOperationType.UnlockInput);
        request.OpenApplication = new OpenApplicationOperationParameters { ApplicationId = "word" };

        var result = await new UnlockInputOperationHandler(
                client,
                NullLogger<UnlockInputOperationHandler>.Instance)
            .HandleAsync(request, CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.ProtocolViolation, result.ErrorCode);
        Assert.Equal(0, client.UnlockCalls);
    }

    [Fact]
    public async Task LockInput_WithExpiredLicense_IsBlockedByDispatcher()
    {
        var client = RecordingSessionCommandClient.Success();
        var dispatcher = CreateDispatcher(client, LicenseState.Blocked(
            CommercialLicenseStatus.LicenseExpired,
            FixedNow,
            "Commercial license expired."));

        var result = await dispatcher.DispatchAsync(
            CreateRequest(NetworkOperationType.LockInput),
            CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.OperationRejected, result.Result.ErrorCode);
        Assert.Equal(0, client.LockCalls);
    }

    [Theory]
    [InlineData(CommercialLicenseStatus.LicenseExpired)]
    [InlineData(CommercialLicenseStatus.ActivationRequired)]
    [InlineData(CommercialLicenseStatus.LicenseInvalid)]
    public async Task UnlockInput_WithInactiveLicense_ReachesHandler(CommercialLicenseStatus status)
    {
        var client = RecordingSessionCommandClient.Success();
        var dispatcher = CreateDispatcher(client, LicenseState.Blocked(
            status,
            FixedNow,
            "Commercial license is not active."));

        var result = await dispatcher.DispatchAsync(
            CreateRequest(NetworkOperationType.UnlockInput),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Result.Status);
        Assert.Equal(1, client.UnlockCalls);
    }

    [Fact]
    public void LicensePolicy_ExceptionIsStrictlyUnlockInput()
    {
        var policy = new RemoteOperationLicensePolicy();

        Assert.False(policy.RequiresActiveCommercialLicense(NetworkOperationType.UnlockInput));
        Assert.True(policy.RequiresActiveCommercialLicense(NetworkOperationType.LockInput));
        Assert.True(policy.RequiresActiveCommercialLicense(NetworkOperationType.OpenApplication));
        Assert.True(policy.RequiresActiveCommercialLicense(NetworkOperationType.OpenUrl));
        Assert.True(policy.RequiresActiveCommercialLicense(NetworkOperationType.Shutdown));
        Assert.True(policy.RequiresActiveCommercialLicense(NetworkOperationType.Restart));
        Assert.True(policy.RequiresActiveCommercialLicense(NetworkOperationType.ApplyBrowserNavigationPolicy));
    }

    private static RemoteOperationDispatcher CreateDispatcher(
        RecordingSessionCommandClient client,
        LicenseState licenseState)
    {
        var clock = new MutableClock(FixedNow);
        return new RemoteOperationDispatcher(
            [
                new LockInputOperationHandler(client, NullLogger<LockInputOperationHandler>.Instance),
                new UnlockInputOperationHandler(client, NullLogger<UnlockInputOperationHandler>.Instance)
            ],
            new RemoteOperationOptions(),
            clock,
            new StaticLicenseStateProvider(licenseState));
    }

    private static OperationRequest CreateRequest(NetworkOperationType operationType)
    {
        return new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = operationType,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds()
        };
    }

    private sealed class RecordingSessionCommandClient : ISessionCommandClient
    {
        private readonly SessionCommandClientResult _result;

        private RecordingSessionCommandClient(SessionCommandClientResult result)
        {
            _result = result;
        }

        public int LockCalls { get; private set; }

        public int UnlockCalls { get; private set; }

        public List<(string OperationId, string Url)> OpenUrlCalls { get; } = [];

        public List<string> OpenApplicationCalls { get; } = [];

        public static RecordingSessionCommandClient Success()
        {
            return new RecordingSessionCommandClient(SessionCommandClientResult.Success());
        }

        public static RecordingSessionCommandClient Failure(string errorCode)
        {
            return new RecordingSessionCommandClient(SessionCommandClientResult.Failure(errorCode));
        }

        public Task<SessionCommandClientResult> OpenUrlAsync(
            string operationId,
            string url,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenUrlCalls.Add((operationId, url));
            return Task.FromResult(_result);
        }

        public Task<SessionCommandClientResult> OpenApplicationAsync(
            string applicationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenApplicationCalls.Add(applicationId);
            return Task.FromResult(_result);
        }

        public Task<SessionCommandClientResult> LockInputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LockCalls++;
            return Task.FromResult(_result);
        }

        public Task<SessionCommandClientResult> UnlockInputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnlockCalls++;
            return Task.FromResult(_result);
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

    private sealed class StaticLicenseStateProvider : ILicenseStateProvider
    {
        public StaticLicenseStateProvider(LicenseState currentState)
        {
            CurrentState = currentState;
        }

        public LicenseState CurrentState { get; }
    }
}
