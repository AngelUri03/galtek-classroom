package com.galtek.classroom.operations;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface BatchOperationRepository {

    void create(String classroomId, BatchOperation operation, OperationPayload payload, OffsetDateTime nowUtc);

    Optional<BatchOperation> findById(String operationId);

    List<BatchTargetResult> findRetryableFailures(String operationId);
}
