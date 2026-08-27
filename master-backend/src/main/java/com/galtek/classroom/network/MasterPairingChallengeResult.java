package com.galtek.classroom.network;

public record MasterPairingChallengeResult(
        boolean created,
        String errorCode,
        String errorMessage,
        PairingChallenge challenge) {

    public static MasterPairingChallengeResult created(PairingChallenge challenge) {
        return new MasterPairingChallengeResult(true, null, null, challenge);
    }

    public static MasterPairingChallengeResult failed(String errorCode, String errorMessage) {
        return new MasterPairingChallengeResult(false, errorCode, errorMessage, null);
    }
}
