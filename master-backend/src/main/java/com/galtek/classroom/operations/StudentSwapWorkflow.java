package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record StudentSwapWorkflow(
        String operationId,
        String leftStudentId,
        String rightStudentId,
        String leftDeviceId,
        String rightDeviceId,
        MoveWorkflowState state,
        ErrorCode errorCode) {

    public StudentSwapWorkflow {
        operationId = requireNonBlank(operationId, "operationId");
        leftStudentId = requireNonBlank(leftStudentId, "leftStudentId");
        rightStudentId = requireNonBlank(rightStudentId, "rightStudentId");
        leftDeviceId = requireNonBlank(leftDeviceId, "leftDeviceId");
        rightDeviceId = requireNonBlank(rightDeviceId, "rightDeviceId");
        requireNonNull(state, "state");
    }
}
