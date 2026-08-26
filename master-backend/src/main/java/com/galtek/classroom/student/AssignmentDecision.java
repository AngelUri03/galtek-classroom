package com.galtek.classroom.student;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.PreflightStatus;
import java.util.List;

public record AssignmentDecision(
        String studentId,
        String deviceId,
        PreflightStatus status,
        List<ErrorCode> errors) {

    public AssignmentDecision {
        studentId = requireNonBlank(studentId, "studentId");
        deviceId = requireNonBlank(deviceId, "deviceId");
        requireNonNull(status, "status");
        errors = copyList(errors, "errors");
    }

    public boolean ready() {
        return status == PreflightStatus.READY;
    }
}
