using System.Runtime.InteropServices;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Service.WindowsSessions;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoWindowsSessionState = GaltekClassroom.Protocol.Network.V1.WindowsSessionState;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class WindowsSessionStateTests : IDisposable
{
    private static readonly Guid InstallationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private const string PrimarySid = "S-1-5-21-1000000000-1000000000-1000000000-1004";
    private const string SecondarySid = "S-1-5-21-1000000000-1000000000-1000000000-1005";
    private const string OtherSid = "S-1-5-21-1000000000-1000000000-1000000000-1006";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.WindowsSessionState.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Resolver_WhenConsoleSessionIsInvalid_ReturnsUnknown()
    {
        var native = FakeNativeApi.WithUser(PrimarySid) with
        {
            ConsoleSessionId = WindowsConsoleSessionResolver.WtsNoSession
        };

        var result = await CreateResolver(native).ObserveAsync(CancellationToken.None);

        Assert.Equal(ConsoleSessionIdentityObservationStatus.Unknown, result.Status);
        Assert.True(result.Succeeded);
        Assert.Equal(0, native.QueryUserTokenCalls);
    }

    [Fact]
    public async Task Resolver_WhenConsoleSessionIsZero_ReturnsUnknown()
    {
        var native = FakeNativeApi.WithUser(PrimarySid) with
        {
            ConsoleSessionId = 0
        };

        var result = await CreateResolver(native).ObserveAsync(CancellationToken.None);

        Assert.Equal(ConsoleSessionIdentityObservationStatus.Unknown, result.Status);
        Assert.True(result.Succeeded);
        Assert.Equal(0, native.QueryUserTokenCalls);
    }

    [Fact]
    public async Task Resolver_WhenUsernameIsEmpty_ReturnsNoSession()
    {
        var native = FakeNativeApi.WithoutUser();

        var result = await CreateResolver(native).ObserveAsync(CancellationToken.None);

        Assert.Equal(ConsoleSessionIdentityObservationStatus.NoSession, result.Status);
        Assert.Null(result.WindowsSid);
        Assert.Equal(1, native.WtsFreeMemoryCalls);
        Assert.Equal(0, native.QueryUserTokenCalls);
    }

    [Fact]
    public async Task Resolver_WhenUsernameQueryFails_ReturnsConservativeFailure()
    {
        var native = FakeNativeApi.WithUser(PrimarySid) with
        {
            QueryUsernameSucceeds = false
        };

        var result = await CreateResolver(native).ObserveAsync(CancellationToken.None);

        Assert.Equal(ConsoleSessionIdentityObservationStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionUnknown, result.ErrorCode);
        Assert.Equal(0, native.QueryUserTokenCalls);
    }

    [Fact]
    public async Task Resolver_WhenUserPresentAndTokenSucceeds_ReturnsTokenSid()
    {
        var native = FakeNativeApi.WithUser(PrimarySid);

        var result = await CreateResolver(native).ObserveAsync(CancellationToken.None);

        Assert.Equal(ConsoleSessionIdentityObservationStatus.User, result.Status);
        Assert.Equal(PrimarySid, result.WindowsSid);
        Assert.Equal(1, native.PrivilegeLeaseDisposeCalls);
        Assert.Equal(1, native.QueryUserTokenCalls);
        Assert.Equal(1, native.CloseHandleCalls);
    }

    [Fact]
    public async Task Resolver_WhenWtsQueryUserTokenFails_ReturnsConservativeFailureAndRestoresPrivilege()
    {
        var native = FakeNativeApi.WithUser(PrimarySid) with
        {
            QueryTokenSucceeds = false
        };

        var result = await CreateResolver(native).ObserveAsync(CancellationToken.None);

        Assert.Equal(ConsoleSessionIdentityObservationStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionUnknown, result.ErrorCode);
        Assert.Equal(1, native.PrivilegeLeaseDisposeCalls);
        Assert.Equal(0, native.CloseHandleCalls);
    }

    [Fact]
    public async Task Resolver_WhenGetTokenInformationFails_ReturnsFailureAndClosesToken()
    {
        var native = FakeNativeApi.WithUser(PrimarySid) with
        {
            SecondGetTokenInformationSucceeds = false
        };

        var result = await CreateResolver(native).ObserveAsync(CancellationToken.None);

        Assert.Equal(ConsoleSessionIdentityObservationStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionUnknown, result.ErrorCode);
        Assert.Equal(1, native.CloseHandleCalls);
        Assert.Equal(1, native.FreeHGlobalCalls);
    }

    [Fact]
    public async Task Resolver_AlwaysReleasesTokenAndNativeBuffers()
    {
        var native = FakeNativeApi.WithUser(PrimarySid);

        var result = await CreateResolver(native).ObserveAsync(CancellationToken.None);

        Assert.Equal(ConsoleSessionIdentityObservationStatus.User, result.Status);
        Assert.Equal(1, native.WtsFreeMemoryCalls);
        Assert.Equal(1, native.CloseHandleCalls);
        Assert.Equal(1, native.LocalFreeCalls);
        Assert.Equal(1, native.FreeHGlobalCalls);
    }

    [Fact]
    public async Task Service_WhenPrimarySidMatches_ReturnsPrimaryActive()
    {
        await SaveBindingsAsync([PrimaryBinding()]);

        var result = await CreateService(PrimarySid).GetStateAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ProtoWindowsSessionState.PrimaryActive, result.State);
    }

    [Fact]
    public async Task Service_WhenSecondarySidMatches_ReturnsSecondaryActive()
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);

        var result = await CreateService(SecondarySid).GetStateAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ProtoWindowsSessionState.SecondaryActive, result.State);
    }

    [Fact]
    public async Task Service_WhenSidIsDifferent_ReturnsOtherSessionActive()
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);

        var result = await CreateService(OtherSid).GetStateAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ProtoWindowsSessionState.OtherSessionActive, result.State);
    }

    [Fact]
    public async Task Service_WhenNoBindingsAndUserExists_ReturnsOtherSessionActive()
    {
        await EnsureInstallationIdentityAsync();

        var result = await CreateService(OtherSid).GetStateAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ProtoWindowsSessionState.OtherSessionActive, result.State);
    }

    [Fact]
    public async Task Service_WhenOnlyPrimaryIsConfigured_DoesNotInferSecondary()
    {
        await SaveBindingsAsync([PrimaryBinding()]);

        var primary = await CreateService(PrimarySid).GetStateAsync(CancellationToken.None);
        var other = await CreateService(SecondarySid).GetStateAsync(CancellationToken.None);

        Assert.Equal(ProtoWindowsSessionState.PrimaryActive, primary.State);
        Assert.Equal(ProtoWindowsSessionState.OtherSessionActive, other.State);
    }

    [Fact]
    public async Task Service_WhenCatalogIsCorrupt_FailsClosed()
    {
        await EnsureInstallationIdentityAsync();
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, ManagedWindowsAccountBindingConstants.FileName),
            "{ corrupt");

        var result = await CreateService(PrimarySid).GetStateAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.ManagedAccountBindingsInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task Service_WhenCatalogIsCorruptAndNoUser_DoesNotReturnNoSession()
    {
        await EnsureInstallationIdentityAsync();
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, ManagedWindowsAccountBindingConstants.FileName),
            "{ corrupt");

        var result = await CreateService(new StaticResolver(ConsoleSessionIdentityObservation.NoSession(4)))
            .GetStateAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.ManagedAccountBindingsInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task Service_MapsBySidAndIgnoresAccountReference()
    {
        await SaveBindingsAsync([
            PrimaryBinding(accountReference: "RENAMED\\Whatever", windowsSid: PrimarySid)
        ]);

        var result = await CreateService(PrimarySid).GetStateAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ProtoWindowsSessionState.PrimaryActive, result.State);
    }

    [Fact]
    public async Task Service_WhenResolverReportsNoSession_ReturnsNoSessionWithoutReadingBindings()
    {
        await EnsureInstallationIdentityAsync();
        var service = CreateService(new StaticResolver(ConsoleSessionIdentityObservation.NoSession(4)));

        var result = await service.GetStateAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ProtoWindowsSessionState.NoSession, result.State);
    }

    [Fact]
    public async Task Service_WhenInstallationIdentityIsMissing_DoesNotCreateIt()
    {
        var result = await CreateService(PrimarySid).GetStateAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionUnknown, result.ErrorCode);
        Assert.False(File.Exists(Path.Combine(_dataDirectory, InstallationIdentityConstants.FileName)));
    }

    [Fact]
    public async Task Service_WhenPhysicalConsoleIsSecondary_IgnoresHistoricalPrimarySession()
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);

        var result = await CreateService(SecondarySid).GetStateAsync(CancellationToken.None);

        Assert.Equal(ProtoWindowsSessionState.SecondaryActive, result.State);
    }

    [Fact]
    public async Task Service_DoesNotUseDisconnectedOrRdpSessionRecords()
    {
        await SaveBindingsAsync([PrimaryBinding(), SecondaryBinding()]);
        var native = FakeNativeApi.WithUser(SecondarySid) with
        {
            ExtraDisconnectedSid = PrimarySid,
            ExtraRdpSid = PrimarySid
        };

        var result = await CreateService(native).GetStateAsync(CancellationToken.None);

        Assert.Equal(ProtoWindowsSessionState.SecondaryActive, result.State);
        Assert.Equal(1, native.ActiveConsoleSessionCalls);
    }

    [Fact]
    public async Task Handler_ReturnsTypedSuccessResult()
    {
        await SaveBindingsAsync([PrimaryBinding()]);
        var handler = new GetWindowsSessionStateOperationHandler(
            CreateService(PrimarySid),
            NullLogger<GetWindowsSessionStateOperationHandler>.Instance);

        var result = await handler.HandleAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Success, result.Status);
        Assert.NotNull(result.WindowsSessionState);
        Assert.Equal(ProtoWindowsSessionState.PrimaryActive, result.WindowsSessionState.State);
    }

    [Fact]
    public async Task Handler_DoesNotAcceptRequestParameters()
    {
        var handler = new GetWindowsSessionStateOperationHandler(
            CreateService(PrimarySid),
            NullLogger<GetWindowsSessionStateOperationHandler>.Instance);
        var request = CreateRequest();
        request.OpenUrl = new OpenUrlOperationParameters { Url = "https://example.edu" };

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.ProtocolViolation, result.ErrorCode);
    }

    [Fact]
    public async Task Handler_PreservesStoreInvalidAsStructuredError()
    {
        await EnsureInstallationIdentityAsync();
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, ManagedWindowsAccountBindingConstants.FileName),
            "{ corrupt");
        var handler = new GetWindowsSessionStateOperationHandler(
            CreateService(PrimarySid),
            NullLogger<GetWindowsSessionStateOperationHandler>.Instance);

        var result = await handler.HandleAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.ManagedAccountBindingsInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task Handler_PreservesResolverFailureAsStructuredError()
    {
        var handler = new GetWindowsSessionStateOperationHandler(
            CreateService(new StaticResolver(ConsoleSessionIdentityObservation.Failed("resolver failed"))),
            NullLogger<GetWindowsSessionStateOperationHandler>.Instance);

        var result = await handler.HandleAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Status);
        Assert.Equal(NetworkOperationErrorCode.WindowsSessionUnknown, result.ErrorCode);
    }

    [Fact]
    public async Task Dispatcher_DedupesWindowsSessionStateByOperationId()
    {
        var handler = new CountingHandler(ProtoWindowsSessionState.Unknown);
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow),
            new StaticLicenseStateProvider(LicenseState.ActiveState(
                "license",
                "org",
                [],
                CommercialLicenseFeatures.Empty,
                FixedNow.AddDays(1),
                FixedNow)));
        var request = CreateRequest();

        RemoteOperationDispatchResult first = await dispatcher.DispatchAsync(request, CancellationToken.None);
        RemoteOperationDispatchResult second = await dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(ProtoWindowsSessionState.Unknown, second.Result.WindowsSessionState.State);
    }

    [Fact]
    public async Task Dispatcher_BlocksWindowsSessionStateWhenLicenseIsInactive()
    {
        var handler = new CountingHandler(ProtoWindowsSessionState.Unknown);
        var dispatcher = new RemoteOperationDispatcher(
            [handler],
            new RemoteOperationOptions(),
            new MutableClock(FixedNow),
            new StaticLicenseStateProvider(LicenseState.Blocked(
                CommercialLicenseStatus.LicenseExpired,
                FixedNow,
                "Commercial license expired.")));

        RemoteOperationDispatchResult result = await dispatcher.DispatchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(OperationExecutionStatus.Failed, result.Result.Status);
        Assert.Equal(NetworkOperationErrorCode.OperationRejected, result.Result.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void LicensePolicy_DoesNotCreateRecoverySafeException()
    {
        var policy = new RemoteOperationLicensePolicy();

        Assert.True(policy.RequiresActiveCommercialLicense(NetworkOperationType.GetWindowsSessionState));
        Assert.False(policy.RequiresActiveCommercialLicense(NetworkOperationType.UnlockInput));
    }

    [Fact]
    public void OperationResultDetails_DoNotExposeRawWindowsIdentity()
    {
        var result = new OperationResult
        {
            OperationId = "operation-1",
            OperationType = NetworkOperationType.GetWindowsSessionState,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            Status = OperationExecutionStatus.Success,
            WindowsSessionState = new WindowsSessionStateResult
            {
                State = ProtoWindowsSessionState.OtherSessionActive
            }
        };

        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("S-1-5", json);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionId", json, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private WindowsConsoleSessionResolver CreateResolver(FakeNativeApi native)
    {
        return new WindowsConsoleSessionResolver(
            native,
            NullLogger<WindowsConsoleSessionResolver>.Instance);
    }

    private WindowsSessionStateService CreateService(string activeSid)
    {
        return CreateService(FakeNativeApi.WithUser(activeSid));
    }

    private WindowsSessionStateService CreateService(FakeNativeApi native)
    {
        return CreateService(CreateResolver(native));
    }

    private WindowsSessionStateService CreateService(IWindowsConsoleSessionResolver resolver)
    {
        return new WindowsSessionStateService(
            resolver,
            new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory)),
            CreateStore());
    }

    private ManagedWindowsAccountBindingStore CreateStore()
    {
        return new ManagedWindowsAccountBindingStore(
            new ManagedWindowsAccountBindingStoreOptions(_dataDirectory),
            new NoOpManagedWindowsAccountBindingFileSecurity());
    }

    private async Task SaveBindingsAsync(IReadOnlyList<ManagedWindowsAccountBinding> bindings)
    {
        await EnsureInstallationIdentityAsync();
        var store = CreateStore();
        foreach (ManagedWindowsAccountBinding binding in bindings)
        {
            var result = await store.AddAsync(InstallationId, binding, CancellationToken.None);
            Assert.True(result.Succeeded);
        }
    }

    private async Task EnsureInstallationIdentityAsync()
    {
        var store = new InstallationIdentityStore(new InstallationIdentityStoreOptions(_dataDirectory));
        var existing = await store.ReadAsync(CancellationToken.None);
        if (existing.Status == InstallationIdentityStoreReadStatus.Loaded)
        {
            return;
        }

        await store.WriteNewAsync(InstallationIdentity.Create(
            InstallationId,
            ValidFingerprint(),
            FixedNow),
            CancellationToken.None);
    }

    private static OperationRequest CreateRequest()
    {
        return new OperationRequest
        {
            OperationId = Guid.NewGuid().ToString("D"),
            OperationType = NetworkOperationType.GetWindowsSessionState,
            TargetDeviceId = "device-1",
            ProtocolVersion = MasterConnectionConstants.ProtocolVersion,
            SentAtUnixMs = FixedNow.ToUnixTimeMilliseconds()
        };
    }

    private static ManagedWindowsAccountBinding PrimaryBinding(
        string accountReference = "PC23\\Primaria",
        string windowsSid = PrimarySid)
    {
        return ManagedWindowsAccountBinding.Create(
            ClassroomManagedWindowsAccountTypes.Primary,
            windowsSid,
            accountReference,
            FixedNow);
    }

    private static ManagedWindowsAccountBinding SecondaryBinding()
    {
        return ManagedWindowsAccountBinding.Create(
            ClassroomManagedWindowsAccountTypes.Secondary,
            SecondarySid,
            "PC23\\Secundaria",
            FixedNow);
    }

    private sealed record FakeNativeApi : IWindowsConsoleSessionNativeApi
    {
        private readonly IntPtr _tokenHandle = new(200);
        private readonly IntPtr _sidPointer = new(300);

        public bool IsWindows { get; init; } = true;
        public uint ConsoleSessionId { get; init; } = 4;
        public string Username { get; init; } = "student";
        public string TokenSid { get; init; } = PrimarySid;
        public bool QueryUsernameSucceeds { get; init; } = true;
        public bool RunningAsLocalSystem { get; init; } = true;
        public bool EnablePrivilegeSucceeds { get; init; } = true;
        public bool QueryTokenSucceeds { get; init; } = true;
        public bool SecondGetTokenInformationSucceeds { get; init; } = true;
        public string? ExtraDisconnectedSid { get; init; }
        public string? ExtraRdpSid { get; init; }

        public int ActiveConsoleSessionCalls { get; private set; }
        public int QueryUserTokenCalls { get; private set; }
        public int WtsFreeMemoryCalls { get; private set; }
        public int CloseHandleCalls { get; private set; }
        public int LocalFreeCalls { get; private set; }
        public int FreeHGlobalCalls { get; private set; }
        public int PrivilegeLeaseDisposeCalls { get; private set; }
        private int _getTokenInformationCalls;

        public static FakeNativeApi WithUser(string windowsSid)
        {
            return new FakeNativeApi { TokenSid = windowsSid };
        }

        public static FakeNativeApi WithoutUser()
        {
            return new FakeNativeApi { Username = string.Empty };
        }

        public uint GetActiveConsoleSessionId()
        {
            ActiveConsoleSessionCalls++;
            return ConsoleSessionId;
        }

        public bool IsRunningAsLocalSystem()
        {
            return RunningAsLocalSystem;
        }

        public bool TryEnableTcbPrivilege(
            out IWindowsPrivilegeLease? lease,
            out string step,
            out int win32Error)
        {
            if (!EnablePrivilegeSucceeds)
            {
                lease = null;
                step = "AdjustTokenPrivileges";
                win32Error = 1300;
                return false;
            }

            lease = new FakePrivilegeLease(this);
            step = string.Empty;
            win32Error = 0;
            return true;
        }

        public bool WtsQuerySessionInformation(
            uint sessionId,
            int infoClass,
            out IntPtr buffer,
            out int bytesReturned)
        {
            if (!QueryUsernameSucceeds)
            {
                buffer = IntPtr.Zero;
                bytesReturned = 0;
                return false;
            }

            buffer = Marshal.StringToHGlobalUni(Username);
            bytesReturned = (Username.Length + 1) * 2;
            return true;
        }

        public void WtsFreeMemory(IntPtr buffer)
        {
            WtsFreeMemoryCalls++;
            Marshal.FreeHGlobal(buffer);
        }

        public bool WtsQueryUserToken(uint sessionId, out IntPtr tokenHandle)
        {
            QueryUserTokenCalls++;
            tokenHandle = QueryTokenSucceeds ? _tokenHandle : IntPtr.Zero;
            return QueryTokenSucceeds;
        }

        public bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength)
        {
            _getTokenInformationCalls++;
            returnLength = Marshal.SizeOf<TokenUser>();
            if (tokenInformation == IntPtr.Zero)
            {
                return false;
            }

            if (!SecondGetTokenInformationSucceeds && _getTokenInformationCalls >= 2)
            {
                return false;
            }

            Marshal.StructureToPtr(
                new TokenUser
                {
                    User = new SidAndAttributes
                    {
                        Sid = _sidPointer
                    }
                },
                tokenInformation,
                fDeleteOld: false);
            return true;
        }

        public bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid)
        {
            if (sid != _sidPointer)
            {
                stringSid = IntPtr.Zero;
                return false;
            }

            stringSid = Marshal.StringToHGlobalUni(TokenSid);
            return true;
        }

        public string? PtrToStringUni(IntPtr pointer)
        {
            return Marshal.PtrToStringUni(pointer);
        }

        public IntPtr AllocHGlobal(int bytes)
        {
            return Marshal.AllocHGlobal(bytes);
        }

        public void FreeHGlobal(IntPtr buffer)
        {
            FreeHGlobalCalls++;
            Marshal.FreeHGlobal(buffer);
        }

        public void LocalFree(IntPtr buffer)
        {
            LocalFreeCalls++;
            Marshal.FreeHGlobal(buffer);
        }

        public void CloseHandle(IntPtr handle)
        {
            CloseHandleCalls++;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SidAndAttributes
        {
            public IntPtr Sid;
            public int Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenUser
        {
            public SidAndAttributes User;
        }

        private sealed class FakePrivilegeLease : IWindowsPrivilegeLease
        {
            private readonly FakeNativeApi _owner;

            public FakePrivilegeLease(FakeNativeApi owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                _owner.PrivilegeLeaseDisposeCalls++;
            }
        }
    }

    private sealed class StaticResolver : IWindowsConsoleSessionResolver
    {
        private readonly ConsoleSessionIdentityObservation _observation;

        public StaticResolver(ConsoleSessionIdentityObservation observation)
        {
            _observation = observation;
        }

        public Task<ConsoleSessionIdentityObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_observation);
        }
    }

    private sealed class CountingHandler : IRemoteOperationHandler
    {
        private readonly ProtoWindowsSessionState _state;

        public CountingHandler(ProtoWindowsSessionState state)
        {
            _state = state;
        }

        public int Calls { get; private set; }

        public NetworkOperationType OperationType => NetworkOperationType.GetWindowsSessionState;

        public Task<RemoteOperationHandlerResult> HandleAsync(
            OperationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(RemoteOperationHandlerResult.Success(
                "Windows session state was observed.",
                new WindowsSessionStateResult { State = _state }));
        }
    }

    private static HardwareFingerprint ValidFingerprint()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        return new HardwareFingerprint(hash, hash, hash, hash);
    }

    private sealed class MutableClock : ISystemClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class StaticLicenseStateProvider : GaltekClassroom.Agent.Service.Licensing.ILicenseStateProvider
    {
        public StaticLicenseStateProvider(LicenseState currentState)
        {
            CurrentState = currentState;
        }

        public LicenseState CurrentState { get; }
    }
}
