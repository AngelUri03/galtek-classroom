package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record OperationTarget(
        OperationTargetType type,
        String targetId,
        String displayName) {

    public OperationTarget {
        requireNonNull(type, "type");
        targetId = requireNonBlank(targetId, "targetId");
        displayName = requireNonBlank(displayName, "displayName");
    }
}
