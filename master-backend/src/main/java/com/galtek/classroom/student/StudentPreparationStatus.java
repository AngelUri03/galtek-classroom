package com.galtek.classroom.student;

public enum StudentPreparationStatus {
    PENDING,
    IN_PROGRESS,
    READY,
    PARTIAL_READY,
    RECOVERY_REQUIRED,
    FAILED;

    public boolean ready() {
        return this == READY;
    }

    public boolean preparing() {
        return this == PENDING || this == IN_PROGRESS;
    }

    public boolean problem() {
        return this == PARTIAL_READY || this == RECOVERY_REQUIRED || this == FAILED;
    }
}
