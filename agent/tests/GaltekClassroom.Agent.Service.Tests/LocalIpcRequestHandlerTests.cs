using System.Text;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Ipc;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.Runtime;
using GaltekClassroom.Agent.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class LocalIpcRequestHandlerTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 8, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.Ipc.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HandleAsync_WhenProtocolVersionIsUnsupported_ReturnsProtocolUnsupported()
    {
        var handler = CreateHandler();
        var requestId = Guid.NewGuid().ToString("D");

        var response = await HandleAsync(handler, new LocalIpcRequest
        {
            ProtocolVersion = 999,
            RequestId = requestId,
            Operation = LocalIpcOperations.Ping
        });

        Assert.False(response.Success);
        Assert.Equal(requestId, response.RequestId);
        Assert.Equal(LocalIpcErrorCodes.ProtocolUnsupported, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_WhenOperationIsUnknown_ReturnsOperationNotSupported()
    {
        var handler = CreateHandler();
        var requestId = Guid.NewGuid().ToString("D");

        var response = await HandleAsync(handler, new LocalIpcRequest
        {
            RequestId = requestId,
            Operation = "DO_SOMETHING_RANDOM"
        });

        Assert.False(response.Success);
        Assert.Equal(requestId, response.RequestId);
        Assert.Equal(LocalIpcErrorCodes.OperationNotSupported, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_WhenRequestIsMalformed_ReturnsMalformedRequest()
    {
        var handler = CreateHandler();

        var responseJson = await handler.HandleAsync("{ not-json", CancellationToken.None);
        var response = JsonSerializer.Deserialize<LocalIpcResponse>(responseJson, JsonOptions)!;

        Assert.False(response.Success);
        Assert.Equal(LocalIpcErrorCodes.MalformedRequest, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_WhenPingSucceeds_PreservesRequestId()
    {
        var handler = CreateHandler();
        var requestId = Guid.NewGuid().ToString("D");

        var response = await HandleAsync(handler, new LocalIpcRequest
        {
            RequestId = requestId,
            Operation = LocalIpcOperations.Ping
        });

        Assert.True(response.Success);
        Assert.Equal(requestId, response.RequestId);
    }

    [Fact]
    public async Task HandleAsync_WhenDeviceStatusIsRequested_ReturnsSafeStatusWithoutSensitiveFields()
    {
        var identity = CreateIdentity();
        var handler = CreateHandler(identity);
        var requestId = Guid.NewGuid().ToString("D");

        var responseJson = await handler.HandleAsync(JsonSerializer.Serialize(new LocalIpcRequest
        {
            RequestId = requestId,
            Operation = LocalIpcOperations.GetDeviceStatus
        }, JsonOptions), CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var json = document.RootElement.GetRawText();
        var payload = document.RootElement.GetProperty("payload");

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(ProductInfo.ProductCode, payload.GetProperty("product").GetString());
        Assert.Equal(identity.InstallationId.ToString("D"), payload.GetProperty("installationId").GetString());
        Assert.Equal("PC-AULA-07", payload.GetProperty("hostname").GetString());
        Assert.Equal("ACTIVATION_REQUIRED", payload.GetProperty("licenseStatus").GetString());
        Assert.Equal("MINIMAL_READY", payload.GetProperty("startupPhase").GetString());
        Assert.False(payload.GetProperty("previousShutdownWasUnclean").GetBoolean());
        Assert.False(payload.GetProperty("recoveryActive").GetBoolean());
        Assert.DoesNotContain("jwt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cpuHash", json, StringComparison.Ordinal);
        Assert.DoesNotContain("motherboardHash", json, StringComparison.Ordinal);
        Assert.DoesNotContain("macHash", json, StringComparison.Ordinal);
        Assert.DoesNotContain("diskHash", json, StringComparison.Ordinal);
        Assert.DoesNotContain(identity.CpuHash, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(identity.MotherboardHash, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(identity.MacHash, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(identity.DiskHash, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_WhenMachineCodeIsRequested_UsesExistingMachineCodeGenerator()
    {
        var identity = CreateIdentity();
        var handler = CreateHandler(identity);
        var requestId = Guid.NewGuid().ToString("D");
        var expected = new MachineCodeGenerator(new FakeHostNameProvider("PC-AULA-07")).Generate(identity);

        var responseJson = await handler.HandleAsync(JsonSerializer.Serialize(new LocalIpcRequest
        {
            RequestId = requestId,
            Operation = LocalIpcOperations.GetMachineCode
        }, JsonOptions), CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);

        Assert.Equal(
            expected,
            document.RootElement.GetProperty("payload").GetProperty("machineCode").GetString());
    }

    [Fact]
    public async Task HandleAsync_WhenRuntimeDiagnosticsIsRequested_ReturnsOnDemandSnapshot()
    {
        var handler = CreateHandler();
        var requestId = Guid.NewGuid().ToString("D");

        var responseJson = await handler.HandleAsync(JsonSerializer.Serialize(new LocalIpcRequest
        {
            RequestId = requestId,
            Operation = LocalIpcOperations.GetRuntimeDiagnostics
        }, JsonOptions), CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(ProductInfo.ProductCode, payload.GetProperty("product").GetString());
        Assert.Equal(ProductInfo.ServiceDisplayName, payload.GetProperty("component").GetString());
        Assert.Equal("ON_DEMAND", payload.GetProperty("samplingMode").GetString());
        Assert.Equal(Environment.ProcessId, payload.GetProperty("processId").GetInt32());
        Assert.True(payload.GetProperty("managedMemoryBytes").GetInt64() >= 0);
        Assert.True(payload.GetProperty("workingSetBytes").GetInt64() > 0);
        Assert.True(payload.GetProperty("threadCount").GetInt32() > 0);
    }

    [Fact]
    public async Task HandleAsync_WhenMasterAuthorizationHasNoBinding_ReturnsNotConfigured()
    {
        var handler = CreateHandler();
        var requestId = Guid.NewGuid().ToString("D");

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = requestId,
                Operation = LocalIpcOperations.GetMasterAuthorization
            }, JsonOptions),
            new LocalIpcClientContext("S-1-5-21-1000000000-1000000000-1000000000-1001", "AULA\\MaestraPrimaria"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(requestId, document.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(LocalIpcProtocol.ProtocolVersion, document.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("NOT_CONFIGURED", payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("authorized").GetBoolean());
        Assert.False(payload.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_WhenMasterLicenseIsMissing_ReturnsBusinessStateWithoutSid()
    {
        var identity = CreateIdentity();
        var store = new MasterBindingStore(
            new MasterBindingStoreOptions(_dataDirectory),
            new NoOpMasterBindingFileSecurity());
        var boundSid = "S-1-5-21-1000000000-1000000000-1000000000-1001";
        await store.WriteAsync(
            MasterWindowsBinding.Create(
                identity.InstallationId,
                boundSid,
                "AULA\\MaestraPrimaria",
                FixedNowUtc),
            replaceExisting: false,
            CancellationToken.None);
        var handler = CreateHandler(identity);
        var requestId = Guid.NewGuid().ToString("D");

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = requestId,
                Operation = LocalIpcOperations.GetMasterAuthorization
            }, JsonOptions),
            new LocalIpcClientContext(boundSid, "AULA\\MaestraPrimaria"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var json = document.RootElement.GetRawText();
        var payload = document.RootElement.GetProperty("payload");

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("MASTER_LICENSE_REQUIRED", payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("authorized").GetBoolean());
        Assert.True(payload.GetProperty("configured").GetBoolean());
        Assert.Equal("AULA\\MaestraPrimaria", payload.GetProperty("boundAccountDisplayName").GetString());
        Assert.Equal("AULA\\MaestraPrimaria", payload.GetProperty("currentAccountDisplayName").GetString());
        Assert.DoesNotContain(boundSid, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_WhenCurrentWindowsAccountDiffers_ReturnsNotAuthorized()
    {
        var identity = CreateIdentity();
        var store = new MasterBindingStore(
            new MasterBindingStoreOptions(_dataDirectory),
            new NoOpMasterBindingFileSecurity());
        var boundSid = "S-1-5-21-1000000000-1000000000-1000000000-1001";
        await store.WriteAsync(
            MasterWindowsBinding.Create(
                identity.InstallationId,
                boundSid,
                "AULA\\MaestraPrimaria",
                FixedNowUtc),
            replaceExisting: false,
            CancellationToken.None);
        var handler = CreateHandler(
            identity,
            LicenseState.ActiveState(
                "license-1",
                "ORG-1",
                [CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole],
                CommercialLicenseFeatures.Empty,
                FixedNowUtc.AddDays(30),
                FixedNowUtc));
        var requestId = Guid.NewGuid().ToString("D");

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = requestId,
                Operation = LocalIpcOperations.GetMasterAuthorization
            }, JsonOptions),
            new LocalIpcClientContext(
                "S-1-5-21-1000000000-1000000000-1000000000-1002",
                "AULA\\Soporte"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal("CURRENT_ACCOUNT_NOT_AUTHORIZED", payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("authorized").GetBoolean());
        Assert.Equal("AULA\\Soporte", payload.GetProperty("currentAccountDisplayName").GetString());
    }

    [Theory]
    [InlineData(CommercialLicenseStatus.Active)]
    [InlineData(CommercialLicenseStatus.LicenseExpired)]
    [InlineData(CommercialLicenseStatus.ActivationRequired)]
    [InlineData(CommercialLicenseStatus.LicenseKeyNotConfigured)]
    public async Task HandleAsync_WhenMasterUnlockAuthorizationIsRequested_AuthorizesBoundSidRegardlessOfLicense(
        CommercialLicenseStatus licenseStatus)
    {
        var identity = CreateIdentity();
        var boundSid = "S-1-5-21-1000000000-1000000000-1000000000-1001";
        await WriteBindingAsync(identity, boundSid);
        var handler = CreateHandler(identity, LicenseStateFor(licenseStatus));
        var requestId = Guid.NewGuid().ToString("D");

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = requestId,
                Operation = LocalIpcOperations.GetMasterUnlockAuthorization
            }, JsonOptions),
            new LocalIpcClientContext(boundSid, "AULA\\MaestraPrimaria"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var json = document.RootElement.GetRawText();
        var payload = document.RootElement.GetProperty("payload");

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(LocalIpcProtocol.ProtocolVersion, document.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("AUTHORIZED", payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("authorized").GetBoolean());
        Assert.True(payload.GetProperty("configured").GetBoolean());
        Assert.DoesNotContain(boundSid, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jwt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("licenseStatus", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("roles", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MASTER", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_WhenMasterUnlockAuthorizationHasNoBinding_ReturnsNotConfigured()
    {
        var handler = CreateHandler();

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = LocalIpcOperations.GetMasterUnlockAuthorization
            }, JsonOptions),
            new LocalIpcClientContext("S-1-5-21-1000000000-1000000000-1000000000-1001", "AULA\\MaestraPrimaria"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("NOT_CONFIGURED", payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("authorized").GetBoolean());
        Assert.False(payload.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_WhenMasterUnlockAuthorizationBindingIsCorrupt_ReturnsBindingInvalid()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, MasterBindingConstants.FileName),
            "{ not-json",
            CancellationToken.None);
        var handler = CreateHandler();

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = LocalIpcOperations.GetMasterUnlockAuthorization
            }, JsonOptions),
            new LocalIpcClientContext("S-1-5-21-1000000000-1000000000-1000000000-1001", "AULA\\MaestraPrimaria"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal("MASTER_BINDING_INVALID", payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("authorized").GetBoolean());
        Assert.True(payload.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_WhenMasterUnlockAuthorizationBindingSchemaIsUnknown_ReturnsBindingInvalid()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, MasterBindingConstants.FileName),
            """
            {
              "schemaVersion": 999,
              "installationId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "windowsSid": "S-1-5-21-1000000000-1000000000-1000000000-1001",
              "accountDisplayName": "AULA\\MaestraPrimaria",
              "boundAtUtc": "2026-08-25T15:00:00Z"
            }
            """,
            CancellationToken.None);
        var handler = CreateHandler();

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = LocalIpcOperations.GetMasterUnlockAuthorization
            }, JsonOptions),
            new LocalIpcClientContext("S-1-5-21-1000000000-1000000000-1000000000-1001", "AULA\\MaestraPrimaria"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal("MASTER_BINDING_INVALID", payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("authorized").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_WhenMasterUnlockAuthorizationInstallationDiffers_ReturnsInstallationMismatch()
    {
        var identity = CreateIdentity();
        await WriteBindingAsync(
            identity with { InstallationId = Guid.Parse("bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee") },
            "S-1-5-21-1000000000-1000000000-1000000000-1001");
        var handler = CreateHandler(identity);

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = LocalIpcOperations.GetMasterUnlockAuthorization
            }, JsonOptions),
            new LocalIpcClientContext("S-1-5-21-1000000000-1000000000-1000000000-1001", "AULA\\MaestraPrimaria"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal("MASTER_BINDING_INSTALLATION_MISMATCH", payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("authorized").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_WhenMasterUnlockAuthorizationCallerSidDiffers_ReturnsNotAuthorized()
    {
        var identity = CreateIdentity();
        await WriteBindingAsync(identity, "S-1-5-21-1000000000-1000000000-1000000000-1001");
        var handler = CreateHandler(identity);

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = LocalIpcOperations.GetMasterUnlockAuthorization
            }, JsonOptions),
            new LocalIpcClientContext("S-1-5-21-1000000000-1000000000-1000000000-1002", "AULA\\OtroAdministrador"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal("CURRENT_ACCOUNT_NOT_AUTHORIZED", payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("authorized").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_WhenMasterUnlockAuthorizationCallerSidIsUnavailable_FailsClosed()
    {
        var identity = CreateIdentity();
        await WriteBindingAsync(identity, "S-1-5-21-1000000000-1000000000-1000000000-1001");
        var handler = CreateHandler(identity);

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = LocalIpcOperations.GetMasterUnlockAuthorization
            }, JsonOptions),
            LocalIpcClientContext.Unavailable(),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal("CURRENT_ACCOUNT_NOT_AUTHORIZED", payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("authorized").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_WhenMasterUnlockAuthorizationPayloadDeclaresDifferentSid_IgnoresPayloadSid()
    {
        var identity = CreateIdentity();
        var boundSid = "S-1-5-21-1000000000-1000000000-1000000000-1001";
        await WriteBindingAsync(identity, boundSid);
        var handler = CreateHandler(identity, LicenseStateFor(CommercialLicenseStatus.LicenseExpired));

        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = LocalIpcOperations.GetMasterUnlockAuthorization,
                Payload = new Dictionary<string, object?>
                {
                    ["windowsSid"] = "S-1-5-21-1000000000-1000000000-1000000000-1002"
                }
            }, JsonOptions),
            new LocalIpcClientContext(boundSid, "AULA\\MaestraPrimaria"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal("AUTHORIZED", payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("authorized").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_WhenMasterUnlockAuthorizationIsRequested_DoesNotModifyBindingFile()
    {
        var identity = CreateIdentity();
        var boundSid = "S-1-5-21-1000000000-1000000000-1000000000-1001";
        var bindingPath = await WriteBindingAsync(identity, boundSid);
        var beforeBytes = await File.ReadAllBytesAsync(bindingPath, CancellationToken.None);
        var beforeWriteTime = File.GetLastWriteTimeUtc(bindingPath);
        var handler = CreateHandler(identity, LicenseStateFor(CommercialLicenseStatus.LicenseExpired));

        _ = await handler.HandleAsync(
            JsonSerializer.Serialize(new LocalIpcRequest
            {
                RequestId = Guid.NewGuid().ToString("D"),
                Operation = LocalIpcOperations.GetMasterUnlockAuthorization
            }, JsonOptions),
            new LocalIpcClientContext(boundSid, "AULA\\MaestraPrimaria"),
            CancellationToken.None);

        var afterBytes = await File.ReadAllBytesAsync(bindingPath, CancellationToken.None);
        var afterWriteTime = File.GetLastWriteTimeUtc(bindingPath);

        Assert.Equal(beforeBytes, afterBytes);
        Assert.Equal(beforeWriteTime, afterWriteTime);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<LocalIpcResponse> HandleAsync(
        LocalIpcRequestHandler handler,
        LocalIpcRequest request)
    {
        var responseJson = await handler.HandleAsync(
            JsonSerializer.Serialize(request, JsonOptions),
            CancellationToken.None);

        return JsonSerializer.Deserialize<LocalIpcResponse>(responseJson, JsonOptions)!;
    }

    private LocalIpcRequestHandler CreateHandler(
        InstallationIdentity? identity = null,
        LicenseState? masterLicenseState = null)
    {
        var runtimeState = new AgentRuntimeState();
        runtimeState.SetInstallationIdentity(identity ?? CreateIdentity());

        var hostNameProvider = new FakeHostNameProvider("PC-AULA-07");
        var clock = new FakeClock(FixedNowUtc);
        var licenseManager = new CommercialLicenseManager(
            new CommercialLicenseStore(new CommercialLicenseStoreOptions(_dataDirectory)),
            new CommercialLicenseValidator(
                new NotConfiguredPublicKeyProvider(),
                clock,
                new StaticHardwareFingerprintProvider(ToFingerprint(runtimeState.GetInstallationIdentity()))),
            clock);

        return new LocalIpcRequestHandler(
            runtimeState,
            licenseManager,
            new MachineCodeGenerator(hostNameProvider),
            hostNameProvider,
            new MasterAuthorizationService(
                runtimeState,
                masterLicenseState is null
                    ? licenseManager
                    : new StaticLicenseStateProvider(masterLicenseState),
                new MasterBindingStore(
                    new MasterBindingStoreOptions(_dataDirectory),
                    new NoOpMasterBindingFileSecurity()),
                NullLogger<MasterAuthorizationService>.Instance),
            NullLogger<LocalIpcRequestHandler>.Instance);
    }

    private async Task<string> WriteBindingAsync(InstallationIdentity identity, string windowsSid)
    {
        var store = new MasterBindingStore(
            new MasterBindingStoreOptions(_dataDirectory),
            new NoOpMasterBindingFileSecurity());

        await store.WriteAsync(
            MasterWindowsBinding.Create(
                identity.InstallationId,
                windowsSid,
                "AULA\\MaestraPrimaria",
                FixedNowUtc),
            replaceExisting: false,
            CancellationToken.None);

        return store.FilePath;
    }

    private static LicenseState LicenseStateFor(CommercialLicenseStatus status)
    {
        if (status == CommercialLicenseStatus.Active)
        {
            return LicenseState.ActiveState(
                "license-1",
                "ORG-1",
                [CommercialLicenseConstants.ClientRole, CommercialLicenseConstants.MasterRole],
                CommercialLicenseFeatures.Empty,
                FixedNowUtc.AddDays(30),
                FixedNowUtc);
        }

        return LicenseState.Blocked(
            status,
            FixedNowUtc,
            "blocked");
    }

    private static InstallationIdentity CreateIdentity()
    {
        return InstallationIdentity.Create(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            HardwareFingerprintFactory.FromRawValues(
                ["CPU SERIAL 1"],
                ["MOTHERBOARD SERIAL 1"],
                ["AA11BB22CC31"],
                ["DISK SERIAL 1"]),
            FixedNowUtc.AddDays(-1));
    }

    private static HardwareFingerprint ToFingerprint(InstallationIdentity identity)
    {
        return new HardwareFingerprint(
            identity.CpuHash,
            identity.MotherboardHash,
            identity.MacHash,
            identity.DiskHash);
    }

    private sealed class NotConfiguredPublicKeyProvider : ILicensePublicKeyProvider
    {
        public Task<LicensePublicKeyResult> GetPublicKeyAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(LicensePublicKeyResult.NotConfigured("No test key configured."));
        }
    }

    private sealed class StaticHardwareFingerprintProvider : IHardwareFingerprintProvider
    {
        private readonly HardwareFingerprint _fingerprint;

        public StaticHardwareFingerprintProvider(HardwareFingerprint fingerprint)
        {
            _fingerprint = fingerprint;
        }

        public Task<HardwareFingerprint> GetCurrentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_fingerprint);
        }
    }

    private sealed class FakeHostNameProvider : IHostNameProvider
    {
        private readonly string _hostName;

        public FakeHostNameProvider(string hostName)
        {
            _hostName = hostName;
        }

        public string GetHostName()
        {
            return _hostName;
        }
    }

    private sealed class FakeClock : ISystemClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
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
