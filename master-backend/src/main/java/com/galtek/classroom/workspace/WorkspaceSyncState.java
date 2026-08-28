package com.galtek.classroom.workspace;

public enum WorkspaceSyncState {
    SYNCED,
    DIRTY_LOCAL,
    SYNCING,
    PENDING_SYNC,
    RECOVERY_REQUIRED,
    CONFLICT,
    ERROR;

    public boolean canonicalSafeForNewMaterialization() {
        return this == SYNCED;
    }

    public boolean requiresLocalWorkingCopyPreservation() {
        return this != SYNCED;
    }
}
