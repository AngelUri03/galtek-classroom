package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record OperationalError(
        ErrorCode errorCode,
        ErrorCategory category,
        boolean retryable,
        String userMessageKey) {

    public OperationalError(ErrorCode errorCode, String userMessageKey) {
        this(errorCode, errorCode.category(), errorCode.retryable(), userMessageKey);
    }

    public OperationalError {
        requireNonNull(errorCode, "errorCode");
        requireNonNull(category, "category");
        userMessageKey = requireNonBlank(userMessageKey, "userMessageKey");
    }
}
