package com.galtek.classroom.workspace;

public enum LogicalWorkspaceDestination {
    WORKSPACE_ROOT,
    DOCUMENTS,
    HOMEWORK,
    WORK,
    DOWNLOADS,
    DESKTOP,
    CLASSROOM_SHARED,
    REMOVABLE_STORAGE;

    public boolean studentScoped() {
        return this != CLASSROOM_SHARED && this != REMOVABLE_STORAGE;
    }

    public boolean authorizedStudentDocumentDestination() {
        return studentScoped() || this == REMOVABLE_STORAGE;
    }

    public boolean requiresRemovableStorageAuthorization() {
        return this == REMOVABLE_STORAGE;
    }
}
