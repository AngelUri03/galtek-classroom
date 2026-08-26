package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record StudentMoveWorkflow(
        String operationId,
        String studentId,
        String sourceDeviceId,
        String targetDeviceId,
        MoveWorkflowState state,
        ErrorCode errorCode) {

    public StudentMoveWorkflow {
        operationId = requireNonBlank(operationId, "operationId");
        studentId = requireNonBlank(studentId, "studentId");
        sourceDeviceId = requireNonBlank(sourceDeviceId, "sourceDeviceId");
        targetDeviceId = requireNonBlank(targetDeviceId, "targetDeviceId");
        requireNonNull(state, "state");
    }
}
