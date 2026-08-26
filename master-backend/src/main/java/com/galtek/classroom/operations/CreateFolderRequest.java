package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.workspace.LogicalWorkspaceDestination;
import java.util.List;

public record CreateFolderRequest(
        String operationId,
        String folderName,
        LogicalWorkspaceDestination logicalDestination,
        ConflictPolicy conflictPolicy,
        List<OperationTarget> targets) {

    public CreateFolderRequest {
        operationId = requireNonBlank(operationId, "operationId");
        folderName = requireNonBlank(folderName, "folderName");
        requireNonNull(logicalDestination, "logicalDestination");
        requireNonNull(conflictPolicy, "conflictPolicy");
        targets = copyList(targets, "targets");
    }
}
