package com.galtek.classroom.network;

import com.galtek.classroom.device.DeviceStatus;
import java.time.Instant;
import java.util.UUID;

public record ClientConnectionSnapshot(
        UUID clientNetworkIdentityId,
        UUID clientInstallationId,
        String deviceId,
        String displayName,
        String hostname,
        DeviceStatus status,
        Instant connectedAtUtc,
        Instant lastHeartbeatUtc,
        Instant disconnectedAtUtc,
        String connectionId,
        String reasonCode) {
}
