package com.galtek.classroom.network;

import java.time.Instant;
import java.util.UUID;

public record PairingResponse(
        int schemaVersion,
        String purpose,
        UUID challengeId,
        UUID masterNetworkIdentityId,
        UUID clientNetworkIdentityId,
        UUID clientInstallationId,
        String masterPublicKeyFingerprint,
        String clientPublicKeyFingerprint,
        String challengeNonceBase64,
        String responseNonceBase64,
        Instant signedAtUtc,
        String clientSignatureBase64) {
}
