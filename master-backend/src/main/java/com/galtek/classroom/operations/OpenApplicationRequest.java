package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;

import java.util.List;

public record OpenApplicationRequest(
        String operationId,
        String applicationId,
        List<OperationTarget> targets) {

    public OpenApplicationRequest {
        operationId = requireNonBlank(operationId, "operationId");
        applicationId = requireNonBlank(applicationId, "applicationId");
        targets = copyList(targets, "targets");
    }
}
