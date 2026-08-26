package com.galtek.classroom.student;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.MoveWorkflowState;
import com.galtek.classroom.operations.PreflightStatus;
import java.util.List;

public record StudentSwapPlan(
        String operationId,
        String leftStudentId,
        String rightStudentId,
        String leftDeviceId,
        String rightDeviceId,
        PreflightStatus status,
        MoveWorkflowState initialState,
        List<ErrorCode> errors) {

    public StudentSwapPlan {
        operationId = requireNonBlank(operationId, "operationId");
        leftStudentId = requireNonBlank(leftStudentId, "leftStudentId");
        rightStudentId = requireNonBlank(rightStudentId, "rightStudentId");
        if (leftDeviceId != null && leftDeviceId.isBlank()) {
            throw new IllegalArgumentException("leftDeviceId cannot be blank.");
        }
        if (rightDeviceId != null && rightDeviceId.isBlank()) {
            throw new IllegalArgumentException("rightDeviceId cannot be blank.");
        }
        requireNonNull(status, "status");
        requireNonNull(initialState, "initialState");
        errors = copyList(errors, "errors");
    }

    public boolean ready() {
        return status == PreflightStatus.READY;
    }
}
