package com.galtek.classroom.operations;

public enum BatchOperationStatus {
    PLANNED,
    PREFLIGHT,
    RUNNING,
    SUCCESS,
    PARTIAL_SUCCESS,
    FAILED,
    CANCELLED,
    ROLLING_BACK,
    ROLLED_BACK
}
