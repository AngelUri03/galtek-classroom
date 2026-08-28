package com.galtek.classroom.network;

import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.Heartbeat;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentMap;

public class ClientConnectionRegistry {

    private final Clock clock;
    private final ConcurrentMap<UUID, ClientConnectionSnapshot> connections = new ConcurrentHashMap<>();

    public ClientConnectionRegistry(Clock clock) {
        this.clock = clock;
    }

    public ClientConnectionSnapshot markConnecting(
            ClientNetworkIdentityDescriptor descriptor,
            ClientHello hello,
            String connectionId) {
        Instant now = clock.instant();
        ClientConnectionSnapshot snapshot = new ClientConnectionSnapshot(
                descriptor.clientNetworkIdentityId(),
                descriptor.clientInstallationId(),
                sanitizeDeviceId(hello.getDeviceId(), descriptor.clientNetworkIdentityId()),
                blankToFallback(hello.getDisplayName(), sanitizeDeviceId(hello.getDeviceId(), descriptor.clientNetworkIdentityId())),
                blankToFallback(hello.getHostname(), ""),
                DeviceStatus.CONNECTING,
                now,
                null,
                null,
                connectionId,
                null);
        connections.put(descriptor.clientNetworkIdentityId(), snapshot);
        return snapshot;
    }

    public ClientConnectionSnapshot markOnline(
            UUID clientNetworkIdentityId,
            String connectionId,
            Heartbeat heartbeat) {
        return connections.compute(clientNetworkIdentityId, (id, current) -> {
            Instant now = clock.instant();
            if (current == null) {
                return new ClientConnectionSnapshot(
                        id,
                        null,
                        id.toString(),
                        id.toString(),
                        "",
                        DeviceStatus.ONLINE,
                        now,
                        now,
                        null,
                        connectionId,
                        null);
            }
            return new ClientConnectionSnapshot(
                    current.clientNetworkIdentityId(),
                    current.clientInstallationId(),
                    current.deviceId(),
                    current.displayName(),
                    current.hostname(),
                    DeviceStatus.ONLINE,
                    current.connectedAtUtc(),
                    now,
                    null,
                    connectionId,
                    null);
        });
    }

    public ClientConnectionSnapshot markOnline(UUID clientNetworkIdentityId, String connectionId) {
        return markOnline(
                clientNetworkIdentityId,
                connectionId,
                Heartbeat.newBuilder().setHeartbeatId("").build());
    }

    public void markOffline(UUID clientNetworkIdentityId, String connectionId, String reasonCode) {
        connections.computeIfPresent(clientNetworkIdentityId, (id, current) -> {
            if (connectionId != null && !connectionId.equals(current.connectionId())) {
                return current;
            }

            return new ClientConnectionSnapshot(
                    current.clientNetworkIdentityId(),
                    current.clientInstallationId(),
                    current.deviceId(),
                    current.displayName(),
                    current.hostname(),
                    DeviceStatus.OFFLINE,
                    current.connectedAtUtc(),
                    current.lastHeartbeatUtc(),
                    clock.instant(),
                    current.connectionId(),
                    reasonCode);
        });
    }

    public int expireTimedOut(Duration heartbeatTimeout) {
        Instant cutoff = clock.instant().minus(heartbeatTimeout);
        int expired = 0;
        for (var entry : connections.entrySet()) {
            UUID id = entry.getKey();
            ClientConnectionSnapshot current = entry.getValue();
            if (current == null || current.status() == DeviceStatus.OFFLINE) {
                continue;
            }
            Instant lastSignal = current.lastHeartbeatUtc() == null
                    ? current.connectedAtUtc()
                    : current.lastHeartbeatUtc();
            if (lastSignal != null && lastSignal.isBefore(cutoff)) {
                ClientConnectionSnapshot offline = new ClientConnectionSnapshot(
                        current.clientNetworkIdentityId(),
                        current.clientInstallationId(),
                        current.deviceId(),
                        current.displayName(),
                        current.hostname(),
                        DeviceStatus.OFFLINE,
                        current.connectedAtUtc(),
                        current.lastHeartbeatUtc(),
                        clock.instant(),
                        current.connectionId(),
                        "HEARTBEAT_TIMEOUT");
                if (connections.replace(id, current, offline)) {
                    expired++;
                }
            }
        }
        return expired;
    }

    public Optional<ClientConnectionSnapshot> find(UUID clientNetworkIdentityId) {
        return Optional.ofNullable(connections.get(clientNetworkIdentityId));
    }

    public Optional<ClientConnectionSnapshot> findByDeviceId(String deviceId) {
        return connections.values().stream()
                .filter(snapshot -> snapshot.deviceId().equals(deviceId))
                .findFirst();
    }

    public List<ClientConnectionSnapshot> snapshots() {
        return List.copyOf(connections.values());
    }

    private static String sanitizeDeviceId(String deviceId, UUID fallback) {
        return blankToFallback(deviceId, fallback.toString());
    }

    private static String blankToFallback(String value, String fallback) {
        return value == null || value.isBlank() ? fallback : value.trim();
    }
}
