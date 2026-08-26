package com.galtek.classroom.workspace;

public enum LogicalWorkspaceDestination {
    WORKSPACE_ROOT,
    DOCUMENTS,
    HOMEWORK,
    WORK,
    DOWNLOADS,
    DESKTOP,
    CLASSROOM_SHARED;

    public boolean studentScoped() {
        return this != CLASSROOM_SHARED;
    }
}
