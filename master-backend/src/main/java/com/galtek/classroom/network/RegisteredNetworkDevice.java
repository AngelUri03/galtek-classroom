package com.galtek.classroom.network;

import com.galtek.classroom.device.DeviceCapability;
import java.time.OffsetDateTime;
import java.util.Set;
import java.util.UUID;

public record RegisteredNetworkDevice(
        String bindingId,
        String deviceId,
        String classroomId,
        UUID installationId,
        UUID networkIdentityId,
        String publicKeyFingerprint,
        String displayName,
        String hostname,
        String agentVersion,
        Set<DeviceCapability> capabilities,
        OffsetDateTime registeredAtUtc,
        OffsetDateTime lastConnectedAtUtc,
        boolean active,
        long version) {
}
