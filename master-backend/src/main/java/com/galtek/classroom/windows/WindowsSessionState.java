package com.galtek.classroom.windows;

public enum WindowsSessionState {
    NO_SESSION,
    PRIMARY_ACTIVE,
    SECONDARY_ACTIVE,
    ADMIN_ACTIVE,
    OTHER_SESSION_ACTIVE,
    UNKNOWN;

    public boolean matches(ManagedWindowsAccountType accountType) {
        return switch (this) {
            case PRIMARY_ACTIVE -> accountType == ManagedWindowsAccountType.PRIMARY;
            case SECONDARY_ACTIVE -> accountType == ManagedWindowsAccountType.SECONDARY;
            case ADMIN_ACTIVE -> accountType == ManagedWindowsAccountType.ADMIN;
            case NO_SESSION, OTHER_SESSION_ACTIVE, UNKNOWN -> false;
        };
    }

    public boolean managedAccountActive() {
        return this == PRIMARY_ACTIVE || this == SECONDARY_ACTIVE || this == ADMIN_ACTIVE;
    }
}
