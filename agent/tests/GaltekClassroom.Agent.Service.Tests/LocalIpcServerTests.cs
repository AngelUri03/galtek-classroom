using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Ipc;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.Runtime;
using GaltekClassroom.Agent.Session.Ipc;
using GaltekClassroom.Agent.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

[SupportedOSPlatform("windows")]
public sealed class LocalIpcServerTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 8, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "GaltekClassroom.Agent.Ipc.Server.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LocalIpcServer_AllowsMultipleClients()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await server.StartAsync(cancellation.Token);

        try
        {
            var firstClient = new LocalAgentIpcClient(pipeName, TimeSpan.FromSeconds(2));
            var secondClient = new LocalAgentIpcClient(pipeName, TimeSpan.FromSeconds(2));

            var responses = await Task.WhenAll(
                firstClient.PingAsync(cancellation.Token),
                secondClient.PingAsync(cancellation.Token));

            Assert.All(responses, response => Assert.Equal("UP", response.Status));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task LocalIpcServer_WhenMalformedJsonArrives_RejectsItAndKeepsServing()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await server.StartAsync(cancellation.Token);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(2000, cancellation.Token);
            await LocalIpcFraming.WriteJsonAsync(pipe, "{ not-json", cancellation.Token);
            var responseJson = await LocalIpcFraming.ReadJsonAsync(pipe, cancellation.Token);
            var response = JsonSerializer.Deserialize<LocalIpcResponse>(
                responseJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            Assert.False(response.Success);
            Assert.Equal(LocalIpcErrorCodes.MalformedRequest, response.ErrorCode);

            var client = new LocalAgentIpcClient(pipeName, TimeSpan.FromSeconds(2));
            var ping = await client.PingAsync(cancellation.Token);

            Assert.Equal("UP", ping.Status);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task LocalIpcServer_WhenClientDisconnectsBeforeRequest_KeepsServing()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await server.StartAsync(cancellation.Token);

        try
        {
            await using (var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous))
            {
                await pipe.ConnectAsync(2000, cancellation.Token);
            }

            var client = new LocalAgentIpcClient(pipeName, TimeSpan.FromSeconds(2));
            var ping = await client.PingAsync(cancellation.Token);

            Assert.Equal("UP", ping.Status);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task LocalIpcServer_WhenInvalidFrameLengthArrives_KeepsServing()
    {
        var pipeName = CreatePipeName();
        using var server = CreateServer(pipeName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await server.StartAsync(cancellation.Token);

        try
        {
            await using (var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous))
            {
                var lengthPrefix = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, LocalIpcProtocol.MaxMessageBytes + 1);

                await pipe.ConnectAsync(2000, cancellation.Token);
                await pipe.WriteAsync(lengthPrefix, cancellation.Token);
                await pipe.FlushAsync(cancellation.Token);
            }

            var client = new LocalAgentIpcClient(pipeName, TimeSpan.FromSeconds(2));
            var ping = await client.PingAsync(cancellation.Token);

            Assert.Equal("UP", ping.Status);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task LocalAgentIpcClient_WhenResponseRequestIdMismatches_RejectsResponse()
    {
        var pipeName = CreatePipeName();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serverTask = Task.Run(async () =>
        {
            await using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipe.WaitForConnectionAsync(cancellation.Token);
            _ = JsonSerializer.Deserialize<LocalIpcRequest>(
                await LocalIpcFraming.ReadJsonAsync(pipe, cancellation.Token),
                jsonOptions);

            var response = LocalIpcResponse.Ok(
                Guid.NewGuid().ToString("D"),
                new LocalIpcPingPayload());
            await LocalIpcFraming.WriteJsonAsync(
                pipe,
                JsonSerializer.Serialize(response, jsonOptions),
                cancellation.Token);
        }, cancellation.Token);

        var client = new LocalAgentIpcClient(pipeName, TimeSpan.FromSeconds(2));

        var exception = await Assert.ThrowsAsync<LocalAgentIpcException>(
            () => client.PingAsync(cancellation.Token));

        Assert.Equal(LocalIpcErrorCodes.ResponseMismatch, exception.ErrorCode);
        await serverTask;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private LocalIpcServer CreateServer(string pipeName)
    {
        var runtimeState = new AgentRuntimeState();
        runtimeState.SetInstallationIdentity(CreateIdentity());
        var hostNameProvider = new FakeHostNameProvider("PC-AULA-07");
        var clock = new FakeClock(FixedNowUtc);
        var licenseManager = new CommercialLicenseManager(
            new CommercialLicenseStore(new CommercialLicenseStoreOptions(_dataDirectory)),
            new CommercialLicenseValidator(
                new NotConfiguredPublicKeyProvider(),
                clock,
                new StaticHardwareFingerprintProvider(ToFingerprint(runtimeState.GetInstallationIdentity()))),
            clock);
        var handler = new LocalIpcRequestHandler(
            runtimeState,
            licenseManager,
            new MachineCodeGenerator(hostNameProvider),
            hostNameProvider,
            new MasterAuthorizationService(
                runtimeState,
                licenseManager,
                new MasterBindingStore(
                    new MasterBindingStoreOptions(_dataDirectory),
                    new NoOpMasterBindingFileSecurity()),
                NullLogger<MasterAuthorizationService>.Instance),
            NullLogger<LocalIpcRequestHandler>.Instance);

        return new LocalIpcServer(
            new LocalIpcServerOptions(pipeName, 2),
            new LocalIpcPipeStreamFactory(),
            new StaticLocalIpcClientIdentityProvider(new LocalIpcClientContext(
                "S-1-5-21-1000000000-1000000000-1000000000-1001",
                "AULA\\MaestraPrimaria")),
            handler,
            NullLogger<LocalIpcServer>.Instance);
    }

    private static string CreatePipeName()
    {
        return $"GaltekClassroom.Agent.Tests.{Guid.NewGuid():N}";
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

    private sealed class StaticLocalIpcClientIdentityProvider : ILocalIpcClientIdentityProvider
    {
        private readonly LocalIpcClientContext _context;

        public StaticLocalIpcClientIdentityProvider(LocalIpcClientContext context)
        {
            _context = context;
        }

        public LocalIpcClientContext GetClientContext(PipeStream pipe)
        {
            return _context;
        }
    }
}
