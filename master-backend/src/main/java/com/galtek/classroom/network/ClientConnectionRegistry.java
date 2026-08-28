package com.galtek.classroom.network;

import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.Heartbeat;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Optional;
import java.util.Set;
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
        return markConnecting(descriptor, hello, connectionId, null);
    }

    public ClientConnectionSnapshot markConnecting(
            ClientNetworkIdentityDescriptor descriptor,
            ClientHello hello,
            String connectionId,
            RegisteredNetworkDevice registeredDevice) {
        Instant now = clock.instant();
        Set<DeviceCapability> capabilities = ClientCapabilityMapper.fromHello(hello);
        boolean registered = registeredDevice != null;
        ClientConnectionSnapshot snapshot = new ClientConnectionSnapshot(
                descriptor.clientNetworkIdentityId(),
                descriptor.clientInstallationId(),
                registered ? registeredDevice.deviceId() : null,
                registered ? registeredDevice.classroomId() : null,
                registered,
                registered
                        ? registeredDevice.displayName()
                        : displayNameFromHello(hello, descriptor.clientNetworkIdentityId()),
                blankToFallback(hello.getHostname(), registered ? registeredDevice.hostname() : ""),
                DeviceStatus.CONNECTING,
                blankToFallback(hello.getAgentVersion(), ""),
                capabilities,
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
                        null,
                        null,
                        false,
                        id.toString(),
                        "",
                        DeviceStatus.ONLINE,
                        "",
                        Set.of(),
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
                    current.classroomId(),
                    current.registered(),
                    current.displayName(),
                    current.hostname(),
                    DeviceStatus.ONLINE,
                    current.agentVersion(),
                    current.capabilities(),
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
                    current.classroomId(),
                    current.registered(),
                    current.displayName(),
                    current.hostname(),
                    DeviceStatus.OFFLINE,
                    current.agentVersion(),
                    current.capabilities(),
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
                        current.classroomId(),
                        current.registered(),
                        current.displayName(),
                        current.hostname(),
                        DeviceStatus.OFFLINE,
                        current.agentVersion(),
                        current.capabilities(),
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
                .filter(snapshot -> snapshot.deviceId() != null && snapshot.deviceId().equals(deviceId))
                .findFirst();
    }

    public void attachRegistration(RegisteredNetworkDevice registeredDevice) {
        if (registeredDevice == null) {
            return;
        }

        connections.computeIfPresent(registeredDevice.networkIdentityId(), (id, current) -> new ClientConnectionSnapshot(
                current.clientNetworkIdentityId(),
                current.clientInstallationId(),
                registeredDevice.deviceId(),
                registeredDevice.classroomId(),
                true,
                registeredDevice.displayName(),
                blankToFallback(current.hostname(), registeredDevice.hostname()),
                current.status(),
                current.agentVersion(),
                current.capabilities(),
                current.connectedAtUtc(),
                current.lastHeartbeatUtc(),
                current.disconnectedAtUtc(),
                current.connectionId(),
                current.reasonCode()));
    }

    public List<ClientConnectionSnapshot> snapshots() {
        return List.copyOf(connections.values());
    }

    private static String displayNameFromHello(ClientHello hello, UUID fallback) {
        return blankToFallback(
                hello.getDisplayName(),
                blankToFallback(hello.getHostname(), fallback.toString()));
    }

    private static String blankToFallback(String value, String fallback) {
        return value == null || value.isBlank() ? fallback : value.trim();
    }
}
