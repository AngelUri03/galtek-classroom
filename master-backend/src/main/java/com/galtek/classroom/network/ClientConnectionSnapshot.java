package com.galtek.classroom.network;

import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.device.DeviceCapability;
import java.time.Instant;
import java.util.Set;
import java.util.UUID;

public record ClientConnectionSnapshot(
        UUID clientNetworkIdentityId,
        UUID clientInstallationId,
        String deviceId,
        String classroomId,
        boolean registered,
        String displayName,
        String hostname,
        DeviceStatus status,
        String agentVersion,
        Set<DeviceCapability> capabilities,
        Instant connectedAtUtc,
        Instant lastHeartbeatUtc,
        Instant disconnectedAtUtc,
        String connectionId,
        String reasonCode) {
}
