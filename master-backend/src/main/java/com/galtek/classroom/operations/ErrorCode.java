package com.galtek.classroom.operations;

public enum ErrorCode {
    INVALID_REQUEST(ErrorCategory.OPERATION, false),

    CLASSROOM_NOT_FOUND(ErrorCategory.CLASSROOM, false),
    CLASSROOM_HAS_ACTIVE_CONTENT(ErrorCategory.CLASSROOM, false),

    GROUP_NOT_FOUND(ErrorCategory.GROUP, false),
    GROUP_HAS_ACTIVE_STUDENTS(ErrorCategory.GROUP, false),

    DEVICE_OFFLINE(ErrorCategory.DEVICE, true),
    DEVICE_NOT_FOUND(ErrorCategory.DEVICE, false),
    AGENT_UNAVAILABLE(ErrorCategory.DEVICE, true),
    SESSION_NOT_AVAILABLE(ErrorCategory.DEVICE, true),
    DEVICE_BUSY(ErrorCategory.DEVICE, true),
    SOURCE_DEVICE_UNAVAILABLE(ErrorCategory.DEVICE, true),
    TARGET_DEVICE_UNAVAILABLE(ErrorCategory.DEVICE, true),

    LICENSE_NOT_ACTIVE(ErrorCategory.LICENSE, false),
    FEATURE_NOT_ALLOWED(ErrorCategory.LICENSE, false),

    STUDENT_NOT_FOUND(ErrorCategory.STUDENT, false),
    STUDENT_NOT_ASSIGNED(ErrorCategory.STUDENT, false),
    STUDENT_ALREADY_ASSIGNED(ErrorCategory.STUDENT, false),
    TARGET_OCCUPIED(ErrorCategory.STUDENT, false),
    ASSIGNMENT_NOT_FOUND(ErrorCategory.STUDENT, false),
    SELF_SWAP_NOT_ALLOWED(ErrorCategory.STUDENT, false),
    SAME_DEVICE_ASSIGNMENT(ErrorCategory.STUDENT, false),

    WORKSPACE_NOT_FOUND(ErrorCategory.WORKSPACE, false),
    WORKSPACE_NOT_READY(ErrorCategory.WORKSPACE, true),
    WORKSPACE_BUSY(ErrorCategory.WORKSPACE, true),
    INSUFFICIENT_DISK_SPACE(ErrorCategory.WORKSPACE, false),
    FILE_LOCKED(ErrorCategory.WORKSPACE, true),
    FILE_WRITE_FAILED(ErrorCategory.WORKSPACE, true),
    TRANSFER_FAILED(ErrorCategory.WORKSPACE, true),
    TRANSFER_INTEGRITY_FAILED(ErrorCategory.WORKSPACE, true),
    SOURCE_PRESERVED(ErrorCategory.WORKSPACE, false),
    ROLLBACK_FAILED(ErrorCategory.WORKSPACE, false),

    BROWSER_NOT_AVAILABLE(ErrorCategory.BROWSER, false),
    BROWSER_PROFILE_NOT_FOUND(ErrorCategory.BROWSER, false),
    BROWSER_PROFILE_NOT_AVAILABLE(ErrorCategory.BROWSER, false),
    BROWSER_PROFILE_NOT_PORTABLE(ErrorCategory.BROWSER, false),
    BROWSER_PROFILE_REAUTH_REQUIRED(ErrorCategory.BROWSER, false),
    BROWSER_REAUTH_REQUIRED(ErrorCategory.BROWSER, false),
    INVALID_URL(ErrorCategory.BROWSER, false),

    APPLICATION_NOT_INSTALLED(ErrorCategory.APPLICATION, false),
    APPLICATION_NOT_ALLOWED(ErrorCategory.APPLICATION, false),
    APPLICATION_START_FAILED(ErrorCategory.APPLICATION, true),

    INVALID_FILE(ErrorCategory.CONTENT, false),
    INVALID_FOLDER_NAME(ErrorCategory.CONTENT, false),
    INVALID_DESTINATION(ErrorCategory.CONTENT, false),
    IMAGE_INVALID(ErrorCategory.CONTENT, false),
    IMAGE_TOO_LARGE(ErrorCategory.CONTENT, false),
    WALLPAPER_APPLY_FAILED(ErrorCategory.CONTENT, true),

    MASTER_NOT_LICENSED(ErrorCategory.AUTHORIZATION, false),
    MASTER_WINDOWS_ACCOUNT_NOT_AUTHORIZED(ErrorCategory.AUTHORIZATION, false),
    MASTER_INSTALLATION_MISMATCH(ErrorCategory.AUTHORIZATION, false),
    MASTER_NOT_PAIRED(ErrorCategory.AUTHORIZATION, false),

    OPERATION_CANCELLED(ErrorCategory.OPERATION, false),
    OPERATION_ALREADY_RUNNING(ErrorCategory.OPERATION, true),
    OPERATION_NOT_FOUND(ErrorCategory.OPERATION, false),

    MASTER_DATABASE_UNAVAILABLE(ErrorCategory.PERSISTENCE, true),
    MASTER_DATABASE_CORRUPT(ErrorCategory.PERSISTENCE, false),
    MASTER_DATABASE_MIGRATION_FAILED(ErrorCategory.PERSISTENCE, false),
    MASTER_DATABASE_BUSY(ErrorCategory.PERSISTENCE, true),
    MASTER_STORAGE_FULL(ErrorCategory.PERSISTENCE, false),
    PERSISTENCE_CONSTRAINT_VIOLATION(ErrorCategory.PERSISTENCE, false),
    CONCURRENT_MODIFICATION(ErrorCategory.PERSISTENCE, false);

    private final ErrorCategory category;
    private final boolean retryable;

    ErrorCode(ErrorCategory category, boolean retryable) {
        this.category = category;
        this.retryable = retryable;
    }

    public ErrorCategory category() {
        return category;
    }

    public boolean retryable() {
        return retryable;
    }
}
