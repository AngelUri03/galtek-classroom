package com.galtek.classroom.workspace;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

public final class WorkspaceSyncSafetyPlanner {

    public WorkspaceCleanupDecision evaluateClientCleanup(
            String workspaceId,
            WorkspaceSyncState currentSyncState,
            boolean syncCompleted,
            boolean verified,
            boolean canonicalCommitted,
            boolean clientConfirmed) {
        workspaceId = requireNonBlank(workspaceId, "workspaceId");
        requireNonNull(currentSyncState, "currentSyncState");

        if (currentSyncState == WorkspaceSyncState.CONFLICT
                || currentSyncState == WorkspaceSyncState.ERROR
                || currentSyncState == WorkspaceSyncState.RECOVERY_REQUIRED) {
            return blocked(workspaceId, currentSyncState, WorkspaceCommitStage.SYNC);
        }

        if (currentSyncState == WorkspaceSyncState.DIRTY_LOCAL
                || currentSyncState == WorkspaceSyncState.SYNCING
                || currentSyncState == WorkspaceSyncState.PENDING_SYNC
                || !syncCompleted) {
            return blocked(workspaceId, WorkspaceSyncState.PENDING_SYNC, WorkspaceCommitStage.SYNC);
        }

        if (!verified) {
            return blocked(workspaceId, WorkspaceSyncState.RECOVERY_REQUIRED, WorkspaceCommitStage.VERIFY);
        }

        if (!canonicalCommitted) {
            return blocked(workspaceId, WorkspaceSyncState.RECOVERY_REQUIRED, WorkspaceCommitStage.COMMIT_CANONICAL);
        }

        if (!clientConfirmed) {
            return blocked(workspaceId, WorkspaceSyncState.RECOVERY_REQUIRED, WorkspaceCommitStage.CONFIRM);
        }

        return new WorkspaceCleanupDecision(
                workspaceId,
                true,
                WorkspaceSyncState.SYNCED,
                WorkspaceCommitStage.CLEANUP_CLIENT,
                false);
    }

    private WorkspaceCleanupDecision blocked(
            String workspaceId,
            WorkspaceSyncState resultingSyncState,
            WorkspaceCommitStage requiredStageBeforeCleanup) {
        return new WorkspaceCleanupDecision(
                workspaceId,
                false,
                resultingSyncState,
                requiredStageBeforeCleanup,
                true);
    }
}
