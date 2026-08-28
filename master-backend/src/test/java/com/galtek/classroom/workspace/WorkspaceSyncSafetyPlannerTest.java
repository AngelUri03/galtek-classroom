package com.galtek.classroom.workspace;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class WorkspaceSyncSafetyPlannerTest {

    private final WorkspaceSyncSafetyPlanner planner = new WorkspaceSyncSafetyPlanner();

    @Test
    void clientWorkingCopyIsNotCleanedBeforeSyncVerifyCommitAndConfirm() {
        var beforeSync = planner.evaluateClientCleanup(
                "WS-STU-001",
                WorkspaceSyncState.DIRTY_LOCAL,
                false,
                false,
                false,
                false);
        var beforeVerify = planner.evaluateClientCleanup(
                "WS-STU-001",
                WorkspaceSyncState.SYNCED,
                true,
                false,
                false,
                false);
        var beforeCommit = planner.evaluateClientCleanup(
                "WS-STU-001",
                WorkspaceSyncState.SYNCED,
                true,
                true,
                false,
                false);

        assertThat(beforeSync.clientCleanupAllowed()).isFalse();
        assertThat(beforeSync.resultingSyncState()).isEqualTo(WorkspaceSyncState.PENDING_SYNC);
        assertThat(beforeSync.requiredStageBeforeCleanup()).isEqualTo(WorkspaceCommitStage.SYNC);
        assertThat(beforeSync.preserveClientWorkingCopy()).isTrue();

        assertThat(beforeVerify.clientCleanupAllowed()).isFalse();
        assertThat(beforeVerify.resultingSyncState()).isEqualTo(WorkspaceSyncState.RECOVERY_REQUIRED);
        assertThat(beforeVerify.requiredStageBeforeCleanup()).isEqualTo(WorkspaceCommitStage.VERIFY);

        assertThat(beforeCommit.clientCleanupAllowed()).isFalse();
        assertThat(beforeCommit.resultingSyncState()).isEqualTo(WorkspaceSyncState.RECOVERY_REQUIRED);
        assertThat(beforeCommit.requiredStageBeforeCleanup()).isEqualTo(WorkspaceCommitStage.COMMIT_CANONICAL);
    }

    @Test
    void missingClientConfirmationRequiresRecoveryAndPreservesLocalCopy() {
        var decision = planner.evaluateClientCleanup(
                "WS-STU-002",
                WorkspaceSyncState.SYNCED,
                true,
                true,
                true,
                false);

        assertThat(decision.clientCleanupAllowed()).isFalse();
        assertThat(decision.resultingSyncState()).isEqualTo(WorkspaceSyncState.RECOVERY_REQUIRED);
        assertThat(decision.requiredStageBeforeCleanup()).isEqualTo(WorkspaceCommitStage.CONFIRM);
        assertThat(decision.preserveClientWorkingCopy()).isTrue();
    }

    @Test
    void confirmedCanonicalCommitAllowsClientCleanup() {
        var decision = planner.evaluateClientCleanup(
                "WS-STU-003",
                WorkspaceSyncState.SYNCED,
                true,
                true,
                true,
                true);

        assertThat(decision.clientCleanupAllowed()).isTrue();
        assertThat(decision.resultingSyncState()).isEqualTo(WorkspaceSyncState.SYNCED);
        assertThat(decision.requiredStageBeforeCleanup()).isEqualTo(WorkspaceCommitStage.CLEANUP_CLIENT);
        assertThat(decision.preserveClientWorkingCopy()).isFalse();
    }
}
