package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.time.OffsetDateTime;
import java.util.List;

public record BatchOperation(
        String operationId,
        OperationType type,
        String requestedBy,
        OffsetDateTime createdAtUtc,
        int targetCount,
        BatchOperationStatus status,
        List<BatchTargetResult> targets) {

    public BatchOperation {
        operationId = requireNonBlank(operationId, "operationId");
        requireNonNull(type, "type");
        requestedBy = requireNonBlank(requestedBy, "requestedBy");
        requireNonNull(createdAtUtc, "createdAtUtc");
        targets = copyList(targets, "targets");
        if (targetCount < 0) {
            throw new IllegalArgumentException("targetCount cannot be negative.");
        }
        if (targetCount != targets.size()) {
            throw new IllegalArgumentException("targetCount must match targets size.");
        }
        requireNonNull(status, "status");
    }

    public static BatchOperation fromTargets(
            String operationId,
            OperationType type,
            String requestedBy,
            OffsetDateTime createdAtUtc,
            List<BatchTargetResult> targets) {
        var copiedTargets = List.copyOf(targets);

        return new BatchOperation(
                operationId,
                type,
                requestedBy,
                createdAtUtc,
                copiedTargets.size(),
                deriveStatus(copiedTargets),
                copiedTargets);
    }

    public List<BatchTargetResult> failedTargets() {
        return targets.stream()
                .filter(BatchTargetResult::failed)
                .toList();
    }

    public List<BatchTargetResult> retryableFailedTargets() {
        return targets.stream()
                .filter(BatchTargetResult::retryable)
                .toList();
    }

    private static BatchOperationStatus deriveStatus(List<BatchTargetResult> targets) {
        if (targets.isEmpty()) {
            return BatchOperationStatus.PLANNED;
        }

        if (targets.stream().allMatch(BatchOperation::completedSuccessfully)) {
            return BatchOperationStatus.SUCCESS;
        }

        if (targets.stream().allMatch(target -> target.status() == TargetExecutionStatus.FAILED)) {
            return BatchOperationStatus.FAILED;
        }

        if (targets.stream().allMatch(target -> target.status() == TargetExecutionStatus.CANCELLED)) {
            return BatchOperationStatus.CANCELLED;
        }

        if (targets.stream().allMatch(target -> target.status() == TargetExecutionStatus.ROLLED_BACK)) {
            return BatchOperationStatus.ROLLED_BACK;
        }

        if (targets.stream().anyMatch(target -> target.status() == TargetExecutionStatus.PENDING)) {
            return BatchOperationStatus.RUNNING;
        }

        if (targets.stream().anyMatch(target -> target.status() == TargetExecutionStatus.FAILED)
                && targets.stream().anyMatch(BatchOperation::completedSuccessfully)) {
            return BatchOperationStatus.PARTIAL_SUCCESS;
        }

        return BatchOperationStatus.RUNNING;
    }

    private static boolean completedSuccessfully(BatchTargetResult target) {
        return target.status() == TargetExecutionStatus.SUCCESS
                || target.status() == TargetExecutionStatus.NO_CHANGE;
    }
}
