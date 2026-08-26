package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record BatchPreflightItem(
        OperationTarget target,
        PreflightStatus status,
        ErrorCode errorCode,
        String message) {

    public BatchPreflightItem {
        requireNonNull(target, "target");
        requireNonNull(status, "status");
        if (status == PreflightStatus.BLOCKED && errorCode == null) {
            throw new IllegalArgumentException("errorCode is required for blocked preflight targets.");
        }
    }
}
