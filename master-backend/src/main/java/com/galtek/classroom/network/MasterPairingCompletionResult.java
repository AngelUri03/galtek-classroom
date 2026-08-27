package com.galtek.classroom.network;

public record MasterPairingCompletionResult(
        boolean paired,
        String errorCode,
        String errorMessage,
        PairingStatus status) {

    public static MasterPairingCompletionResult success() {
        return new MasterPairingCompletionResult(true, null, null, PairingStatus.PAIRED);
    }

    public static MasterPairingCompletionResult failed(
            String errorCode,
            String errorMessage,
            PairingStatus status) {
        return new MasterPairingCompletionResult(false, errorCode, errorMessage, status);
    }
}
