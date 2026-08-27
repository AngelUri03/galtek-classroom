package com.galtek.classroom.network;

import java.time.Instant;
import java.util.UUID;

public record PairingChallenge(
        int schemaVersion,
        String purpose,
        UUID challengeId,
        UUID masterNetworkIdentityId,
        UUID clientNetworkIdentityId,
        UUID clientInstallationId,
        String masterPublicKeyFingerprint,
        String clientPublicKeyFingerprint,
        String masterPublicKeySubjectPublicKeyInfoBase64,
        String clientPublicKeySubjectPublicKeyInfoBase64,
        String nonceBase64,
        Instant issuedAtUtc,
        Instant expiresAtUtc,
        String masterSignatureBase64) {
}
