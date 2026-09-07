package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record StoredBatchOperation(
        String classroomId,
        BatchOperation operation,
        OperationPayload payload,
        long version) {

    public StoredBatchOperation {
        classroomId = requireNonBlank(classroomId, "classroomId");
        requireNonNull(operation, "operation");
        if (version < 0) {
            throw new IllegalArgumentException("version cannot be negative.");
        }
    }
}
