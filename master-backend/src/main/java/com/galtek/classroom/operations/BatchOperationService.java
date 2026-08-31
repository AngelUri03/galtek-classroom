package com.galtek.classroom.operations;

import java.time.Clock;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class BatchOperationService {

    private final BatchOperationRepository batchOperationRepository;
    private final Clock clock;

    public BatchOperationService(BatchOperationRepository batchOperationRepository, Clock clock) {
        this.batchOperationRepository = batchOperationRepository;
        this.clock = clock;
    }

    @Transactional
    public BatchOperation create(String classroomId, BatchOperation operation, OperationPayload payload) {
        batchOperationRepository.create(classroomId, operation, payload, nowUtc());
        return operation;
    }

    @Transactional
    public BatchOperation replaceResults(BatchOperation operation) {
        batchOperationRepository.replaceResults(operation, nowUtc());
        return operation;
    }

    public Optional<BatchOperation> findById(String operationId) {
        return batchOperationRepository.findById(operationId);
    }

    public List<BatchTargetResult> retryableFailures(String operationId) {
        return batchOperationRepository.findRetryableFailures(operationId);
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }
}
