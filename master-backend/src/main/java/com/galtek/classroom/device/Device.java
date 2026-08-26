package com.galtek.classroom.device;

import static com.galtek.classroom.domain.DomainChecks.copySet;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.time.OffsetDateTime;
import java.util.Set;

public record Device(
        String deviceId,
        String installationId,
        String displayName,
        String hostname,
        DeviceStatus status,
        OffsetDateTime lastSeen,
        Set<DeviceCapability> capabilities,
        String assignedStudentId) {

    public Device {
        deviceId = requireNonBlank(deviceId, "deviceId");
        installationId = requireNonBlank(installationId, "installationId");
        displayName = requireNonBlank(displayName, "displayName");
        hostname = requireNonBlank(hostname, "hostname");
        requireNonNull(status, "status");
        requireNonNull(lastSeen, "lastSeen");
        capabilities = copySet(capabilities, "capabilities");
        if (assignedStudentId != null && assignedStudentId.isBlank()) {
            throw new IllegalArgumentException("assignedStudentId cannot be blank.");
        }
    }

    public boolean availableForInteractiveOperation() {
        return status.availableForInteractiveOperation();
    }

    public Device withAssignedStudentId(String studentId) {
        return new Device(
                deviceId,
                installationId,
                displayName,
                hostname,
                status,
                lastSeen,
                capabilities,
                studentId);
    }
}
