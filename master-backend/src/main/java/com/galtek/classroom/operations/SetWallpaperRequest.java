package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;

import java.util.List;

public record SetWallpaperRequest(
        String operationId,
        String imageReference,
        List<OperationTarget> targets) {

    public SetWallpaperRequest {
        operationId = requireNonBlank(operationId, "operationId");
        imageReference = requireNonBlank(imageReference, "imageReference");
        targets = copyList(targets, "targets");
    }
}
