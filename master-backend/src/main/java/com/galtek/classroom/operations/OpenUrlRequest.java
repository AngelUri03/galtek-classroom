package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;

import java.util.List;

public record OpenUrlRequest(
        String operationId,
        String url,
        String browserProfileId,
        List<OperationTarget> targets) {

    public OpenUrlRequest {
        operationId = requireNonBlank(operationId, "operationId");
        url = requireNonBlank(url, "url");
        browserProfileId = requireNonBlank(browserProfileId, "browserProfileId");
        targets = copyList(targets, "targets");
    }
}
