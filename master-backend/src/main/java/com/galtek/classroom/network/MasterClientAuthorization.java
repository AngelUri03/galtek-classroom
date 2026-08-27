package com.galtek.classroom.network;

import com.galtek.classroom.operations.ErrorCode;

public record MasterClientAuthorization(
        boolean authorized,
        PairingStatus status,
        ErrorCode errorCode,
        String message) {

    public static MasterClientAuthorization success() {
        return new MasterClientAuthorization(true, PairingStatus.PAIRED, null, null);
    }

    public static MasterClientAuthorization blocked(
            PairingStatus status,
            ErrorCode errorCode,
            String message) {
        return new MasterClientAuthorization(false, status, errorCode, message);
    }
}
