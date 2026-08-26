package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.workspace.LogicalWorkspaceDestination;
import java.util.List;

public record DistributeFileRequest(
        String operationId,
        String sourceFileReference,
        LogicalWorkspaceDestination logicalDestination,
        ConflictPolicy conflictPolicy,
        List<OperationTarget> targets) {

    public DistributeFileRequest {
        operationId = requireNonBlank(operationId, "operationId");
        sourceFileReference = requireNonBlank(sourceFileReference, "sourceFileReference");
        requireNonNull(logicalDestination, "logicalDestination");
        requireNonNull(conflictPolicy, "conflictPolicy");
        targets = copyList(targets, "targets");
    }
}
