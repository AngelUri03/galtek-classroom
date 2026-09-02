using GaltekClassroom.Agent.Session.Ipc;
using GaltekClassroom.Agent.Session.Commands;
using GaltekClassroom.Agent.Session.Lifecycle;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class SessionAgentBackgroundHostTests
{
    [Fact]
    public async Task RunAsync_WhenInstanceAlreadyExists_ExitsWithoutStartingSupervisor()
    {
        var client = new CountingLocalAgentClient();
        var host = CreateHost(
            acquired: false,
            sessionId: 12,
            client);

        var exitCode = await host.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, client.PingCalls);
    }

    [Fact]
    public async Task RunAsync_WhenProcessIsInSessionZero_ExitsWithErrorWithoutStartingSupervisor()
    {
        var client = new CountingLocalAgentClient();
        var commandServer = new CountingSessionCommandServer();
        var host = CreateHost(
            acquired: true,
            sessionId: 0,
            client,
            commandServer);

        var exitCode = await host.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, client.PingCalls);
        Assert.Equal(0, commandServer.StartCalls);
    }

    private static SessionAgentBackgroundHost CreateHost(
        bool acquired,
        int sessionId,
        CountingLocalAgentClient client,
        CountingSessionCommandServer? commandServer = null)
    {
        return new SessionAgentBackgroundHost(
            new FakeSessionInstanceLock(acquired),
            new SessionContext(1234, sessionId, "angel"),
            new SessionAgentSupervisor(
                client,
                new NeverDelay(),
                new SessionAgentSupervisorOptions()),
            commandServer);
    }

    private sealed class FakeSessionInstanceLock : ISessionInstanceLock
    {
        private readonly bool _acquired;

        public FakeSessionInstanceLock(bool acquired)
        {
            _acquired = acquired;
        }

        public SessionInstanceLockHandle TryAcquire()
        {
            return new SessionInstanceLockHandle(_acquired, mutex: null);
        }
    }

    private sealed class NeverDelay : ISessionAgentDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class CountingLocalAgentClient : ILocalAgentIpcClient
    {
        public int PingCalls { get; private set; }

        public Task<LocalIpcPingPayload> PingAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(TryPing(cancellationToken));
        }

        public Task<LocalAgentIpcResult<LocalIpcPingPayload>> TryPingAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(LocalAgentIpcResult<LocalIpcPingPayload>.Success(
                TryPing(cancellationToken)));
        }

        public Task<LocalDeviceStatus> GetDeviceStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new LocalDeviceStatus());
        }

        public Task<LocalAgentIpcResult<LocalDeviceStatus>> TryGetDeviceStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(LocalAgentIpcResult<LocalDeviceStatus>.Success(new LocalDeviceStatus()));
        }

        private LocalIpcPingPayload TryPing(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PingCalls++;
            return new LocalIpcPingPayload();
        }
    }

    private sealed class CountingSessionCommandServer : ISessionCommandServer
    {
        public int StartCalls { get; private set; }

        public Task RunAsync(int sessionId, CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
