package com.galtek.classroom.network;

import com.galtek.classroom.device.DeviceCapability;
import java.time.OffsetDateTime;
import java.util.Set;
import java.util.UUID;

public record DeviceNetworkBinding(
        String bindingId,
        String deviceId,
        UUID installationId,
        UUID networkIdentityId,
        String publicKeyFingerprint,
        String agentVersion,
        Set<DeviceCapability> capabilities,
        OffsetDateTime registeredAtUtc,
        OffsetDateTime lastConnectedAtUtc,
        boolean current,
        long version) {
}
