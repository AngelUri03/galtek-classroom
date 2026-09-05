package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.util.Comparator;

public enum OperationPriority {
    LOW(100),
    NORMAL(200),
    HIGH(300),
    CRITICAL(400);

    private final int rank;

    OperationPriority(int rank) {
        this.rank = rank;
    }

    public int rank() {
        return rank;
    }

    public boolean outranks(OperationPriority other) {
        requireNonNull(other, "other");

        return rank > other.rank;
    }

    public static Comparator<OperationPriority> mostImportantFirst() {
        return Comparator.comparingInt(OperationPriority::rank).reversed();
    }

    public static OperationPriority defaultFor(OperationType operationType) {
        requireNonNull(operationType, "operationType");

        return switch (operationType) {
            case UNLOCK_INPUT, STOP_PROJECTION -> CRITICAL;
            case ASSIGN_STUDENT, MOVE_STUDENT, SWAP_STUDENTS, SYNC_STUDENT_WORKSPACE,
                    RESTORE_STUDENT_WORKSPACE, GET_WINDOWS_SESSION_STATE, LOGON_MANAGED_ACCOUNT,
                    LOGOFF_WINDOWS_SESSION, SWITCH_MANAGED_ACCOUNT, PROVISION_MANAGED_CREDENTIAL -> HIGH;
            case LOCK_INPUT, SHUTDOWN, RESTART, OPEN_APPLICATION, OPEN_URL, APPLY_BROWSER_NAVIGATION_POLICY,
                    APPLY_BROWSER_DOWNLOAD_POLICY, START_PROJECTION, DISTRIBUTE_FILE, CREATE_FOLDER,
                    SET_WALLPAPER, RESTORE_WALLPAPER -> NORMAL;
        };
    }
}
