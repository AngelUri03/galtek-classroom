package com.galtek.classroom.workspace;

public record WorkspaceRecoveryPolicy(
        boolean lightweightHistoryPlanned,
        boolean controlledTrashPlanned,
        boolean limitedSnapshotsPlanned,
        boolean restoreLastValidStatePlanned,
        boolean recoverTeacherDistributedFilesPlanned) {

    public static WorkspaceRecoveryPolicy plannedDefaults() {
        return new WorkspaceRecoveryPolicy(true, true, true, true, true);
    }
}
