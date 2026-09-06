package com.galtek.classroom.windows;

import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.TargetExecutionStatus;
import java.util.List;

public final class ManagedAccountSwitchDtos {

    private ManagedAccountSwitchDtos() {
    }

    public record ManagedAccountSwitchBatchResponse(
            String operationId,
            String type,
            String targetAccountId,
            String status,
            int targetCount,
            ManagedAccountSwitchSummaryResponse summary,
            List<ManagedAccountSwitchTargetResponse> targets) {

        public static ManagedAccountSwitchBatchResponse from(
                BatchOperation operation,
                ManagedWindowsAccountType targetAccountType) {
            return new ManagedAccountSwitchBatchResponse(
                    operation.operationId(),
                    operation.type().name(),
                    targetAccountType.name(),
                    operation.status().name(),
                    operation.targetCount(),
                    ManagedAccountSwitchSummaryResponse.from(operation),
                    operation.targets().stream()
                            .map(ManagedAccountSwitchTargetResponse::from)
                            .toList());
        }
    }

    public record ManagedAccountSwitchSummaryResponse(
            int total,
            int noChange,
            int success,
            int failed) {

        static ManagedAccountSwitchSummaryResponse from(BatchOperation operation) {
            int noChange = (int) operation.targets().stream()
                    .filter(target -> target.status() == TargetExecutionStatus.NO_CHANGE)
                    .count();
            int success = (int) operation.targets().stream()
                    .filter(target -> target.status() == TargetExecutionStatus.SUCCESS)
                    .count();
            int failed = (int) operation.targets().stream()
                    .filter(BatchTargetResult::failed)
                    .count();
            return new ManagedAccountSwitchSummaryResponse(operation.targetCount(), noChange, success, failed);
        }
    }

    public record ManagedAccountSwitchTargetResponse(
            String deviceId,
            String status,
            String errorCode,
            boolean retryable,
            String message,
            int attempt) {

        static ManagedAccountSwitchTargetResponse from(BatchTargetResult target) {
            return new ManagedAccountSwitchTargetResponse(
                    target.target().targetId(),
                    target.status().name(),
                    target.errorCode() == null ? null : target.errorCode().name(),
                    target.retryable(),
                    target.message(),
                    target.attempt());
        }
    }
}
