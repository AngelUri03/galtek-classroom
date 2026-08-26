package com.galtek.classroom.student;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.time.OffsetDateTime;

public record DeviceAssignment(
        String studentId,
        String deviceId,
        OffsetDateTime assignedAtUtc,
        DeviceAssignmentStatus status,
        DeviceAssignmentSource source,
        boolean current) {

    public DeviceAssignment {
        studentId = requireNonBlank(studentId, "studentId");
        deviceId = requireNonBlank(deviceId, "deviceId");
        requireNonNull(assignedAtUtc, "assignedAtUtc");
        requireNonNull(status, "status");
        requireNonNull(source, "source");
        if (current && status == DeviceAssignmentStatus.ENDED) {
            throw new IllegalArgumentException("ended assignments cannot be current.");
        }
    }

    public boolean activeCurrent() {
        return current && (status == DeviceAssignmentStatus.CURRENT || status == DeviceAssignmentStatus.PLANNED);
    }
}
