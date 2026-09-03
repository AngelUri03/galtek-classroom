using GaltekClassroom.Agent.Service.Applications;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.SessionCommands;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class OpenApplicationOperationHandlerTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.OpenApplicationHandler.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HandleAsync_WhenApplicationIdIsInvalid_DoesNotUseSessionCommand()
    {
        var client = RecordingSessionCommandClient.Success();
        var handler = CreateHandler(client);

        var result = await handler.HandleAsync(CreateRequest(@"..\bad"), CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.ApplicationBindingInvalid, result.ErrorCode);
        Assert.Empty(client.OpenApplicationCalls);
    }

    [Fact]
    public async Task HandleAsync_WhenBindingIsMissing_DoesNotUseSessionCommand()
    {
        var client = RecordingSessionCommandClient.Success();
        var handler = CreateHandler(client);

        var result = await handler.HandleAsync(CreateRequest("word"), CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.ApplicationBindingNotFound, result.ErrorCode);
        Assert.Empty(client.OpenApplicationCalls);
    }

    [Fact]
    public async Task HandleAsync_WhenBindingIsDisabled_DoesNotUseSessionCommand()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);
        await store.SetEnabledAsync("word", enabled: false, CancellationToken.None);
        var client = RecordingSessionCommandClient.Success();

        var result = await CreateHandler(client).HandleAsync(CreateRequest("word"), CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.ApplicationDisabled, result.ErrorCode);
        Assert.Empty(client.OpenApplicationCalls);
    }

    [Fact]
    public async Task HandleAsync_WhenBindingIsValid_SendsOnlyApplicationId()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("conejito-lector", "Conejito.exe", replaceExisting: false, CancellationToken.None);
        var client = RecordingSessionCommandClient.Success();

        var result = await CreateHandler(client).HandleAsync(CreateRequest("conejito-lector"), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.Equal(["conejito-lector"], client.OpenApplicationCalls);
    }

    [Fact]
    public async Task HandleAsync_WhenSessionReportsExecutableMissing_PreservesError()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);
        var client = RecordingSessionCommandClient.Failure(SessionCommandErrorCodes.ApplicationExecutableNotFound);

        var result = await CreateHandler(client).HandleAsync(CreateRequest("word"), CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.ApplicationExecutableNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Dispatcher_WhenSameOperationIdAndApplicationIdRepeats_DoesNotLaunchTwice()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);
        var client = RecordingSessionCommandClient.Success();
        var dispatcher = CreateDispatcher(client);
        var request = CreateRequest("word", Guid.NewGuid().ToString("D"));

        var first = await dispatcher.DispatchAsync(request, CancellationToken.None);
        var second = await dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, first.Result.Status);
        Assert.Equal(OperationExecutionStatus.Success, second.Result.Status);
        Assert.True(second.Duplicate);
        Assert.Single(client.OpenApplicationCalls);
    }

    [Fact]
    public async Task Dispatcher_WhenSameOperationIdUsesDifferentApplicationId_ReturnsDuplicateConflict()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);
        await store.AddAppPathAsync("scratch", "scratch.exe", replaceExisting: false, CancellationToken.None);
        var client = RecordingSessionCommandClient.Success();
        var dispatcher = CreateDispatcher(client);
        var operationId = Guid.NewGuid().ToString("D");

        _ = await dispatcher.DispatchAsync(CreateRequest("word", operationId), CancellationToken.None);
        var second = await dispatcher.DispatchAsync(CreateRequest("scratch", operationId), CancellationToken.None);

        Assert.True(second.Duplicate);
        Assert.Equal(NetworkOperationErrorCode.OperationDuplicate, second.Result.ErrorCode);
        Assert.Single(client.OpenApplicationCalls);
    }

    [Fact]
    public async Task Dispatcher_WhenCommercialLicenseIsInactive_DoesNotReachHandler()
    {
        var store = CreateStore();
        await store.AddAppPathAsync("word", "WINWORD.EXE", replaceExisting: false, CancellationToken.None);
        var client = RecordingSessionCommandClient.Success();
        var dispatcher = CreateDispatcher(
            client,
            new StaticLicenseStateProvider(LicenseState.Blocked(
                CommercialLicenseStatus.ActivationRequired,
                FixedNow,
                "Commercial license has not been resolved yet.")));

        var dispatch = await dispatcher.DispatchAsync(CreateRequest("word"), CancellationToken.None);

        Assert.Equal(NetworkOperationErrorCode.OperationRejected, dispatch.Result.ErrorCode);
        Assert.Empty(client.OpenApplicationCalls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private OpenApplicationOperationHandler CreateHandler(RecordingSessionCommandClient client)
    {
        return new OpenApplicationOperationHandler(
            CreateStore(),
            client,
            NullLogger<OpenApplicationOperationHandler>.Instance);
    }

    private RemoteOperationDispatcher CreateDispatcher(
        RecordingSessionCommandClient client,
        ILicenseStateProvider? licenseStateProvider = null)
    {
        return new RemoteOperationDispatcher(
            [CreateHandler(client)],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow),
            licenseStateProvider);
    }

    private ApplicationBindingStore CreateStore()
    {
        return new ApplicationBindingStore(
            new ApplicationBindingStoreOptions(_dataDirectory),
            new NoOpApplicationBindingFileSecurity(),
            new MutableClock(FixedNow));
    }

    private static OperationRequest CreateRequest(string applicationId, string? operationId = null)
    {
        return new OperationRequest
        {
            OperationId = operationId ?? Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.OpenApplication,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds(),
            OpenApplication = new OpenApplicationOperationParameters
            {
                ApplicationId = applicationId
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
            return Task.FromResult(_result);
        }

        public Task<SessionCommandClientResult> UnlockInputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
