package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record BatchTargetResult(
        OperationTarget target,
        TargetExecutionStatus status,
        ErrorCode errorCode,
        String message,
        int attempt) {

    public BatchTargetResult {
        requireNonNull(target, "target");
        requireNonNull(status, "status");
        if ((status == TargetExecutionStatus.FAILED
                || status == TargetExecutionStatus.PARTIAL
                || status == TargetExecutionStatus.UNKNOWN) && errorCode == null) {
            throw new IllegalArgumentException("errorCode is required for failed, partial, and unknown targets.");
        }
        if (attempt < 1) {
            throw new IllegalArgumentException("attempt must be at least 1.");
        }
    }

    public boolean failed() {
        return status == TargetExecutionStatus.FAILED
                || status == TargetExecutionStatus.PARTIAL
                || status == TargetExecutionStatus.UNKNOWN;
    }

    public boolean retryable() {
        return status == TargetExecutionStatus.FAILED && errorCode != null && errorCode.retryable();
    }
}
