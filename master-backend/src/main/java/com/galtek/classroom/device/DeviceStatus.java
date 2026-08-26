package com.galtek.classroom.device;

import com.galtek.classroom.operations.ErrorCode;

public enum DeviceStatus {
    ONLINE,
    OFFLINE,
    CONNECTING,
    UNLICENSED,
    LICENSE_BLOCKED,
    AGENT_UNAVAILABLE,
    SESSION_UNAVAILABLE,
    BUSY,
    ERROR;

    public boolean availableForInteractiveOperation() {
        return this == ONLINE;
    }

    public ErrorCode toOperationalError() {
        return switch (this) {
            case ONLINE -> throw new IllegalStateException("ONLINE devices do not map to an operational error.");
            case OFFLINE -> ErrorCode.DEVICE_OFFLINE;
            case CONNECTING, AGENT_UNAVAILABLE -> ErrorCode.AGENT_UNAVAILABLE;
            case UNLICENSED, LICENSE_BLOCKED -> ErrorCode.LICENSE_NOT_ACTIVE;
            case SESSION_UNAVAILABLE -> ErrorCode.SESSION_NOT_AVAILABLE;
            case BUSY -> ErrorCode.DEVICE_BUSY;
            case ERROR -> ErrorCode.AGENT_UNAVAILABLE;
        };
    }
}
