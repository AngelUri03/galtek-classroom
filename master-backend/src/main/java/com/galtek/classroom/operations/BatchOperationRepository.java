package com.galtek.classroom.operations;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface BatchOperationRepository {

    void create(String classroomId, BatchOperation operation, OperationPayload payload, OffsetDateTime nowUtc);

    void replaceResults(BatchOperation operation, OffsetDateTime nowUtc);

    Optional<BatchOperation> findById(String operationId);

    Optional<StoredBatchOperation> findStoredById(String operationId);

    List<BatchTargetResult> findRetryableFailures(String operationId);

    StoredBatchOperation claimRetryTargets(
            String operationId,
            long expectedVersion,
            List<BatchTargetResult> expectedTargets,
            OffsetDateTime nowUtc);

    StoredBatchOperation finishRetryTargets(
            String operationId,
            List<BatchTargetResult> finalTargets,
            OffsetDateTime nowUtc);

    List<BatchOperation> findPowerOperationsWithUnknownTarget(String deviceId);

    List<BatchOperation> findPowerOperationsWithPendingTargetsCreatedBefore(OffsetDateTime recoveryCutoffUtc);
}
