package com.galtek.classroom.network;

import java.time.Instant;
import java.util.List;
import java.util.UUID;

public record MasterTrustDocument(
        int schemaVersion,
        UUID masterNetworkIdentityId,
        String masterPublicKeyFingerprint,
        List<ClientTrustRecord> pairedClients,
        List<MasterPairingChallengeRecord> pairingChallenges) {

    public static MasterTrustDocument empty(MasterNetworkIdentityMetadata masterIdentity) {
        return new MasterTrustDocument(
                PairingConstants.SCHEMA_VERSION,
                masterIdentity.masterNetworkIdentityId(),
                masterIdentity.publicKeyFingerprint(),
                List.of(),
                List.of());
    }
}

record ClientTrustRecord(
        int schemaVersion,
        PairingStatus status,
        UUID masterNetworkIdentityId,
        UUID clientNetworkIdentityId,
        UUID clientInstallationId,
        String masterPublicKeyFingerprint,
        String clientPublicKeyFingerprint,
        String clientPublicKeySubjectPublicKeyInfoBase64,
        Instant pairedAtUtc,
        Instant revokedAtUtc,
        UUID pairedChallengeId,
        String certificateThumbprint) {
}

record MasterPairingChallengeRecord(
        int schemaVersion,
        PairingStatus status,
        UUID challengeId,
        UUID masterNetworkIdentityId,
        UUID clientNetworkIdentityId,
        UUID clientInstallationId,
        String masterPublicKeyFingerprint,
        String clientPublicKeyFingerprint,
        String clientPublicKeySubjectPublicKeyInfoBase64,
        String nonceBase64,
        Instant issuedAtUtc,
        Instant expiresAtUtc,
        Instant consumedAtUtc) {
}
