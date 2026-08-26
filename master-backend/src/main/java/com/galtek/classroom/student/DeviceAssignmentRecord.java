package com.galtek.classroom.student;

import java.time.OffsetDateTime;

public record DeviceAssignmentRecord(
        String assignmentId,
        DeviceAssignment assignment,
        OffsetDateTime endedAtUtc,
        OffsetDateTime createdAtUtc,
        OffsetDateTime updatedAtUtc,
        long version) {
}
