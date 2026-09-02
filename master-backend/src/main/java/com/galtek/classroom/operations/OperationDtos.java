package com.galtek.classroom.operations;

import java.util.List;

public final class OperationDtos {

    private OperationDtos() {
    }

    public record OperationBatchResponse(
            String operationId,
            String type,
            String status,
            int targetCount,
            int successCount,
            int failedCount,
            List<OperationTargetResultResponse> targets) {

        public static OperationBatchResponse from(BatchOperation operation) {
            int successCount = (int) operation.targets().stream()
                    .filter(target -> target.status() == TargetExecutionStatus.SUCCESS
                            || target.status() == TargetExecutionStatus.NO_CHANGE)
                    .count();
            int failedCount = (int) operation.targets().stream()
                    .filter(BatchTargetResult::failed)
                    .count();
            return new OperationBatchResponse(
                    operation.operationId(),
                    operation.type().name(),
                    operation.status().name(),
                    operation.targetCount(),
                    successCount,
                    failedCount,
                    operation.targets().stream()
                            .map(OperationTargetResultResponse::from)
                            .toList());
        }
    }

    public record OperationTargetResultResponse(
            String deviceId,
            String status,
            String errorCode,
            String message,
            int attempt) {

        public static OperationTargetResultResponse from(BatchTargetResult target) {
            return new OperationTargetResultResponse(
                    target.target().targetId(),
                    target.status().name(),
                    target.errorCode() == null ? null : target.errorCode().name(),
                    target.message(),
                    target.attempt());
        }
    }
}
