package com.galtek.classroom.network;

import java.time.Instant;

final class MasterTrustValidator {

    private MasterTrustValidator() {
    }

    static boolean isValid(MasterTrustDocument document) {
        if (document == null
                || document.schemaVersion() != PairingConstants.SCHEMA_VERSION
                || document.masterNetworkIdentityId() == null
                || !NetworkIdentityCrypto.isValidSha256Hex(document.masterPublicKeyFingerprint())
                || document.pairedClients() == null
                || document.pairingChallenges() == null) {
            return false;
        }

        for (ClientTrustRecord client : document.pairedClients()) {
            if (!isValid(client, document)) {
                return false;
            }
        }

        for (MasterPairingChallengeRecord challenge : document.pairingChallenges()) {
            if (!isValid(challenge, document)) {
                return false;
            }
        }

        return true;
    }

    private static boolean isValid(ClientTrustRecord client, MasterTrustDocument document) {
        if (client == null
                || client.schemaVersion() != PairingConstants.SCHEMA_VERSION
                || client.status() == null
                || client.masterNetworkIdentityId() == null
                || client.clientNetworkIdentityId() == null
                || client.clientInstallationId() == null
                || !client.masterNetworkIdentityId().equals(document.masterNetworkIdentityId())
                || !client.masterPublicKeyFingerprint().equals(document.masterPublicKeyFingerprint())
                || !NetworkIdentityCrypto.isValidSha256Hex(client.clientPublicKeyFingerprint())
                || client.clientPublicKeySubjectPublicKeyInfoBase64() == null
                || client.clientPublicKeySubjectPublicKeyInfoBase64().isBlank()) {
            return false;
        }

        try {
            if (!client.clientPublicKeyFingerprint().equals(NetworkIdentityCrypto.publicKeyFingerprintBase64(
                    client.clientPublicKeySubjectPublicKeyInfoBase64()))) {
                return false;
            }
        } catch (IllegalArgumentException exception) {
            return false;
        }

        if ((client.status() == PairingStatus.PAIRED || client.status() == PairingStatus.REVOKED)
                && client.pairedAtUtc() == null) {
            return false;
        }

        return client.status() != PairingStatus.REVOKED || client.revokedAtUtc() != null;
    }

    private static boolean isValid(MasterPairingChallengeRecord challenge, MasterTrustDocument document) {
        return challenge != null
                && challenge.schemaVersion() == PairingConstants.SCHEMA_VERSION
                && challenge.status() != null
                && challenge.challengeId() != null
                && challenge.masterNetworkIdentityId() != null
                && challenge.clientNetworkIdentityId() != null
                && challenge.clientInstallationId() != null
                && challenge.masterNetworkIdentityId().equals(document.masterNetworkIdentityId())
                && challenge.masterPublicKeyFingerprint().equals(document.masterPublicKeyFingerprint())
                && NetworkIdentityCrypto.isValidSha256Hex(challenge.clientPublicKeyFingerprint())
                && publicKeyMatchesFingerprint(
                        challenge.clientPublicKeySubjectPublicKeyInfoBase64(),
                        challenge.clientPublicKeyFingerprint())
                && NetworkIdentityCrypto.isValidNonce(challenge.nonceBase64())
                && challenge.issuedAtUtc() != null
                && challenge.expiresAtUtc() != null
                && challenge.expiresAtUtc().isAfter(challenge.issuedAtUtc())
                && (challenge.status() != PairingStatus.PAIRED || challenge.consumedAtUtc() != null)
                && !Instant.EPOCH.equals(challenge.issuedAtUtc());
    }

    private static boolean publicKeyMatchesFingerprint(String subjectPublicKeyInfoBase64, String fingerprint) {
        try {
            return fingerprint.equals(NetworkIdentityCrypto.publicKeyFingerprintBase64(subjectPublicKeyInfoBase64));
        } catch (IllegalArgumentException exception) {
            return false;
        }
    }
}
