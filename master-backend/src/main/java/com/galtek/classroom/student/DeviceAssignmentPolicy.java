package com.galtek.classroom.student;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.PreflightStatus;
import java.util.ArrayList;
import java.util.Collection;

public final class DeviceAssignmentPolicy {

    public AssignmentDecision evaluateNewAssignment(
            String studentId,
            String deviceId,
            Collection<DeviceAssignment> existingAssignments) {
        var validatedStudentId = requireNonBlank(studentId, "studentId");
        var validatedDeviceId = requireNonBlank(deviceId, "deviceId");
        requireNonNull(existingAssignments, "existingAssignments");

        var errors = new ArrayList<ErrorCode>();

        var studentAlreadyAssigned = existingAssignments.stream()
                .filter(DeviceAssignment::activeCurrent)
                .anyMatch(assignment -> assignment.studentId().equals(validatedStudentId));
        if (studentAlreadyAssigned) {
            errors.add(ErrorCode.STUDENT_ALREADY_ASSIGNED);
        }

        var deviceOccupied = existingAssignments.stream()
                .filter(DeviceAssignment::activeCurrent)
                .anyMatch(assignment -> assignment.deviceId().equals(validatedDeviceId));
        if (deviceOccupied) {
            errors.add(ErrorCode.TARGET_OCCUPIED);
        }

        return new AssignmentDecision(
                validatedStudentId,
                validatedDeviceId,
                errors.isEmpty() ? PreflightStatus.READY : PreflightStatus.BLOCKED,
                errors);
    }
}
