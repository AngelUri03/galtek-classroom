package com.galtek.classroom.network;

import java.time.OffsetDateTime;
import java.util.Set;

public final class NetworkDtos {

    private NetworkDtos() {
    }

    public record NetworkClientResponse(
            String networkIdentityId,
            String trustStatus,
            String connectionStatus,
            String registrationStatus,
            boolean registered,
            String deviceId,
            String classroomId,
            String displayName,
            String agentVersion,
            Set<String> capabilities,
            OffsetDateTime lastSeenUtc,
            OffsetDateTime lastConnectedUtc) {
    }

    public record RegisterDeviceRequest(
            String networkIdentityId,
            String displayName) {
    }

    public record DeviceRegistrationResponse(
            String deviceId,
            String classroomId,
            String networkIdentityId,
            String installationId,
            String displayName,
            String agentVersion,
            Set<String> capabilities,
            OffsetDateTime registeredAtUtc,
            OffsetDateTime lastConnectedUtc) {
    }
}
