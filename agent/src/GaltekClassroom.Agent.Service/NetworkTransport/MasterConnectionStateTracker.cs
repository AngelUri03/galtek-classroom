namespace GaltekClassroom.Agent.Service.NetworkTransport;

public enum MasterConnectionState
{
    Offline,
    Connecting,
    Online
}

public sealed record MasterConnectionSnapshot(
    MasterConnectionState State,
    Guid? MasterNetworkIdentityId,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? LastHeartbeatAckUtc,
    string? ReasonCode,
    string? Message);

public sealed class MasterConnectionStateTracker
{
    private readonly object _sync = new();
    private MasterConnectionSnapshot _snapshot = new(
        MasterConnectionState.Offline,
        null,
        null,
        null,
        null,
        null);

    public MasterConnectionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public void SetConnecting(Guid masterNetworkIdentityId, DateTimeOffset nowUtc)
    {
        Set(new MasterConnectionSnapshot(
            MasterConnectionState.Connecting,
            masterNetworkIdentityId,
            nowUtc.ToUniversalTime(),
            null,
            null,
            null));
    }

    public void SetOnline(Guid masterNetworkIdentityId, DateTimeOffset nowUtc)
    {
        var utc = nowUtc.ToUniversalTime();
        Set(new MasterConnectionSnapshot(
            MasterConnectionState.Online,
            masterNetworkIdentityId,
            Snapshot.ConnectedAtUtc ?? utc,
            utc,
            null,
            null));
    }

    public void SetOffline(
        Guid? masterNetworkIdentityId,
        DateTimeOffset nowUtc,
        string? reasonCode,
        string? message)
    {
        Set(new MasterConnectionSnapshot(
            MasterConnectionState.Offline,
            masterNetworkIdentityId,
            null,
            Snapshot.LastHeartbeatAckUtc,
            reasonCode,
            message));
    }

    private void Set(MasterConnectionSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
        }
    }
}
