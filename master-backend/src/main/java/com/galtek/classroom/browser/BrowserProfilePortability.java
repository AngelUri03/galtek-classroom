package com.galtek.classroom.browser;

import com.galtek.classroom.operations.ErrorCode;

public enum BrowserProfilePortability {
    PORTABLE,
    NOT_PORTABLE,
    REAUTH_REQUIRED,
    UNKNOWN;

    public ErrorCode toOperationalError() {
        return switch (this) {
            case PORTABLE -> throw new IllegalStateException("Portable profiles do not map to an error.");
            case NOT_PORTABLE -> ErrorCode.BROWSER_PROFILE_NOT_PORTABLE;
            case REAUTH_REQUIRED -> ErrorCode.BROWSER_PROFILE_REAUTH_REQUIRED;
            case UNKNOWN -> ErrorCode.BROWSER_PROFILE_NOT_AVAILABLE;
        };
    }
}
