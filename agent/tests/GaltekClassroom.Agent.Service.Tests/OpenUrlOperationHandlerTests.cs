using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.OpenUrl;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class OpenUrlOperationHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WhenUrlIsInvalid_DoesNotUseSessionChannel()
    {
        var client = RecordingSessionCommandClient.Success();
        var handler = CreateHandler(client);

        RemoteOperationHandlerResult result = await handler.HandleAsync(
            CreateRequest(Guid.NewGuid().ToString("D"), "javascript:alert(1)"),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.InvalidUrl, result.ErrorCode);
        Assert.Empty(client.OpenUrlCalls);
    }

    [Fact]
    public async Task HandleAsync_WhenUrlIsValid_UsesSessionCommandClient()
    {
        var client = RecordingSessionCommandClient.Success();
        var handler = CreateHandler(client);
        var operationId = Guid.NewGuid().ToString("D");

        RemoteOperationHandlerResult result = await handler.HandleAsync(
            CreateRequest(operationId, "https://example.test/activity"),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(NetworkOperationErrorCode.Unspecified, result.ErrorCode);
        var call = Assert.Single(client.OpenUrlCalls);
        Assert.Equal(operationId, call.OperationId);
        Assert.Equal("https://example.test/activity", call.Url);
    }

    [Fact]
    public async Task HandleAsync_WhenSessionAgentUnavailable_MapsStructuredFailure()
    {
        RemoteOperationHandlerResult result = await HandleWithSessionFailureAsync(
            SessionCommandErrorCodes.SessionAgentUnavailable);

        Assert.Equal(NetworkOperationErrorCode.SessionAgentUnavailable, result.ErrorCode);
    }

    [Theory]
    [InlineData(SessionCommandErrorCodes.InvalidUrl, NetworkOperationErrorCode.InvalidUrl)]
    [InlineData(SessionCommandErrorCodes.SessionChannelUnauthorized, NetworkOperationErrorCode.SessionChannelUnauthorized)]
    [InlineData(SessionCommandErrorCodes.SessionChannelProtocolMismatch, NetworkOperationErrorCode.SessionChannelProtocolMismatch)]
    [InlineData(SessionCommandErrorCodes.SessionChannelInvalidResponse, NetworkOperationErrorCode.SessionChannelInvalidResponse)]
    [InlineData(SessionCommandErrorCodes.UrlLaunchFailed, NetworkOperationErrorCode.UrlLaunchFailed)]
    public async Task HandleAsync_WhenSessionCommandFails_MapsStructuredFailure(
        string sessionErrorCode,
        NetworkOperationErrorCode expectedErrorCode)
    {
        RemoteOperationHandlerResult result = await HandleWithSessionFailureAsync(sessionErrorCode);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_WhenLocalResultIsUncertain_ReturnsUnknown()
    {
        RemoteOperationHandlerResult result = await HandleWithSessionFailureAsync(
            SessionCommandErrorCodes.SessionCommandResultUnknown);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.SessionCommandResultUnknown, result.ErrorCode);
    }

    [Fact]
    public async Task Dispatcher_WhenSameOperationIdRepeats_DoesNotOpenTwice()
    {
        var client = RecordingSessionCommandClient.Success();
        var dispatcher = CreateDispatcher(client);
        OperationRequest request = CreateRequest(Guid.NewGuid().ToString("D"), "https://example.test/activity");

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(request, CancellationToken.None);
        RemoteOperationDispatchResult second = await dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, first.Result.Status);
        Assert.Equal(OperationExecutionStatus.Success, second.Result.Status);
        Assert.True(second.Duplicate);
        Assert.Single(client.OpenUrlCalls);
    }

    [Fact]
    public async Task Dispatcher_WhenSameOperationIdUsesDifferentUrl_ReturnsDuplicateConflict()
    {
        var client = RecordingSessionCommandClient.Success();
        var dispatcher = CreateDispatcher(client);

        var operationId = Guid.NewGuid().ToString("D");

        _ = await dispatcher.DispatchAsync(
            CreateRequest(operationId, "https://example.test/one"),
            CancellationToken.None);
        RemoteOperationDispatchResult second = await dispatcher.DispatchAsync(
            CreateRequest(operationId, "https://example.test/two"),
            CancellationToken.None);

        Assert.True(second.Duplicate);
        Assert.Equal(NetworkOperationErrorCode.OperationDuplicate, second.Result.ErrorCode);
        Assert.Single(client.OpenUrlCalls);
    }

    [Fact]
    public async Task Dispatcher_WhenCommercialLicenseIsInactive_DoesNotReachHandler()
    {
        var client = RecordingSessionCommandClient.Success();
        var dispatcher = CreateDispatcher(
            client,
            new StaticLicenseStateProvider(LicenseState.Blocked(
                CommercialLicenseStatus.ActivationRequired,
                FixedNow,
                "Commercial license has not been resolved yet.")));

        RemoteOperationDispatchResult dispatch = await dispatcher.DispatchAsync(
            CreateRequest(Guid.NewGuid().ToString("D"), "https://example.test/activity"),
            CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, dispatch.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.OperationRejected, dispatch.Result.ErrorCode);
        Assert.Empty(client.OpenUrlCalls);
    }

    private static async Task<RemoteOperationHandlerResult> HandleWithSessionFailureAsync(string sessionErrorCode)
    {
        var client = RecordingSessionCommandClient.Failure(sessionErrorCode);
        var handler = CreateHandler(client);

        return await handler.HandleAsync(
            CreateRequest(Guid.NewGuid().ToString("D"), "https://example.test/activity"),
            CancellationToken.None);
    }

    private static OpenUrlOperationHandler CreateHandler(RecordingSessionCommandClient client)
    {
        return new OpenUrlOperationHandler(
            client,
            NullLogger<OpenUrlOperationHandler>.Instance);
    }

    private static RemoteOperationDispatcher CreateDispatcher(
        RecordingSessionCommandClient client,
        ILicenseStateProvider? licenseStateProvider = null)
    {
        var clock = new MutableClock(FixedNow);
        return new RemoteOperationDispatcher(
            [CreateHandler(client)],
            new RemoteOperationOptions(),
            clock,
            licenseStateProvider);
    }

    private static OperationRequest CreateRequest(string operationId, string url)
    {
        return new OperationRequest
        {
            OperationId = operationId,
            OperationType = NetworkOperationType.OpenUrl,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds(),
            OpenUrl = new OpenUrlOperationParameters
            {
                Url = url
            }
        };
    }

    private sealed class RecordingSessionCommandClient : ISessionCommandClient
    {
        private readonly SessionCommandClientResult _result;

        private RecordingSessionCommandClient(SessionCommandClientResult result)
        {
            _result = result;
        }

        public List<OpenUrlCall> OpenUrlCalls { get; } = [];

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
            OpenUrlCalls.Add(new OpenUrlCall(operationId, url));
            return Task.FromResult(_result);
        }

        public Task<SessionCommandClientResult> OpenApplicationAsync(
            string applicationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed record OpenUrlCall(string OperationId, string Url);

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
