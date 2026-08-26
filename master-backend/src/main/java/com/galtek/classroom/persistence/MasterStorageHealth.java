package com.galtek.classroom.persistence;

public record MasterStorageHealth(
        MasterStorageStatus status,
        String errorCode) {

    public static MasterStorageHealth ready() {
        return new MasterStorageHealth(MasterStorageStatus.READY, null);
    }

    public static MasterStorageHealth unavailable() {
        return new MasterStorageHealth(MasterStorageStatus.UNAVAILABLE, null);
    }
}
