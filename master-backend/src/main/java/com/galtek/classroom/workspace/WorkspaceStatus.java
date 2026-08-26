package com.galtek.classroom.workspace;

public enum WorkspaceStatus {
    NOT_CREATED,
    PREPARING,
    READY,
    BUSY,
    RECOVERY_REQUIRED,
    ERROR;

    public boolean ready() {
        return this == READY;
    }

    public boolean busy() {
        return this == BUSY || this == PREPARING;
    }
}
