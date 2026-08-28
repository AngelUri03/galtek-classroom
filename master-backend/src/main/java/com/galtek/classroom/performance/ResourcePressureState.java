package com.galtek.classroom.performance;

public enum ResourcePressureState {
    NORMAL,
    DEGRADED;

    public boolean impliesOffline() {
        return false;
    }
}
