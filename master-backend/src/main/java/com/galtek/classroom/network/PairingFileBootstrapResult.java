package com.galtek.classroom.network;

import java.time.Instant;
import java.util.UUID;

public record PairingFileBootstrapResult(
        boolean succeeded,
        String operation,
        String errorCode,
        String errorMessage,
        PairingStatus status,
        String outputPath,
        UUID challengeId,
        UUID masterNetworkIdentityId,
        UUID clientNetworkIdentityId,
        UUID clientInstallationId,
        Instant expiresAtUtc) {

    static PairingFileBootstrapResult challengeCreated(
            String outputPath,
            PairingChallenge challenge) {
        return new PairingFileBootstrapResult(
                true,
                "PAIRING_CREATE_CHALLENGE",
                null,
                null,
                PairingStatus.PAIRING_PENDING,
                outputPath,
                challenge.challengeId(),
                challenge.masterNetworkIdentityId(),
                challenge.clientNetworkIdentityId(),
                challenge.clientInstallationId(),
                challenge.expiresAtUtc());
    }

    static PairingFileBootstrapResult pairingCompleted(PairingResponse response) {
        return new PairingFileBootstrapResult(
                true,
                "PAIRING_COMPLETE",
                null,
                null,
                PairingStatus.PAIRED,
                null,
                response.challengeId(),
                response.masterNetworkIdentityId(),
                response.clientNetworkIdentityId(),
                response.clientInstallationId(),
                null);
    }

    static PairingFileBootstrapResult failed(
            String operation,
            String errorCode,
            String errorMessage,
            PairingStatus status) {
        return new PairingFileBootstrapResult(
                false,
                operation,
                errorCode,
                errorMessage,
                status,
                null,
                null,
                null,
                null,
                null,
                null);
    }
}
