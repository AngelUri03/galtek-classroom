package com.galtek.classroom.operations;

public enum MoveWorkflowState {
    PLANNED,
    PREFLIGHT,
    COPYING,
    VERIFYING,
    PREPARING_TARGET,
    COMMITTING,
    COMPLETED,
    FAILED,
    ROLLING_BACK,
    ROLLED_BACK
}
