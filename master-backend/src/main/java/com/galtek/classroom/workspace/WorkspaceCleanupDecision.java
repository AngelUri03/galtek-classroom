package com.galtek.classroom.workspace;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public record WorkspaceCleanupDecision(
        String workspaceId,
        boolean clientCleanupAllowed,
        WorkspaceSyncState resultingSyncState,
        WorkspaceCommitStage requiredStageBeforeCleanup,
        boolean preserveClientWorkingCopy) {

    public WorkspaceCleanupDecision {
        workspaceId = requireNonBlank(workspaceId, "workspaceId");
        requireNonNull(resultingSyncState, "resultingSyncState");
        requireNonNull(requiredStageBeforeCleanup, "requiredStageBeforeCleanup");
        if (clientCleanupAllowed && preserveClientWorkingCopy) {
            throw new IllegalArgumentException("Allowed cleanup cannot require preserving the client working copy.");
        }
        if (!clientCleanupAllowed && !preserveClientWorkingCopy) {
            throw new IllegalArgumentException("Blocked cleanup must preserve the client working copy.");
        }
    }
}
