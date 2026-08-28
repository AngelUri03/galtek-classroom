package com.galtek.classroom.workspace;

public enum WorkspaceResidencyState {
    NOT_MATERIALIZED,
    MATERIALIZING,
    READY,
    RECOVERY_REQUIRED,
    ERROR;

    public boolean localWorkingCopyReady() {
        return this == READY;
    }
}
