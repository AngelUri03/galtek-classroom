package com.galtek.classroom.student;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.MoveWorkflowState;
import com.galtek.classroom.operations.PreflightStatus;
import java.util.List;

public record StudentMovePlan(
        String operationId,
        String studentId,
        String sourceDeviceId,
        String targetDeviceId,
        PreflightStatus status,
        MoveWorkflowState initialState,
        List<ErrorCode> errors) {

    public StudentMovePlan {
        operationId = requireNonBlank(operationId, "operationId");
        studentId = requireNonBlank(studentId, "studentId");
        if (sourceDeviceId != null && sourceDeviceId.isBlank()) {
            throw new IllegalArgumentException("sourceDeviceId cannot be blank.");
        }
        targetDeviceId = requireNonBlank(targetDeviceId, "targetDeviceId");
        requireNonNull(status, "status");
        requireNonNull(initialState, "initialState");
        errors = copyList(errors, "errors");
    }

    public boolean ready() {
        return status == PreflightStatus.READY;
    }
}
