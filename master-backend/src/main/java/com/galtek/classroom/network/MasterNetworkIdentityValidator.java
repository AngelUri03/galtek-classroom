package com.galtek.classroom.network;

import java.time.Instant;

public final class MasterNetworkIdentityValidator {

    private MasterNetworkIdentityValidator() {
    }

    public static boolean isValid(MasterNetworkIdentityMetadata metadata, boolean requireDerivedKeyId) {
        if (metadata == null
                || metadata.schemaVersion() != PairingConstants.SCHEMA_VERSION
                || metadata.masterNetworkIdentityId() == null
                || !NetworkIdentityCrypto.isValidSha256Hex(metadata.keyId())
                || !NetworkIdentityCrypto.isValidSha256Hex(metadata.publicKeyFingerprint())
                || metadata.publicKeySubjectPublicKeyInfoBase64() == null
                || metadata.publicKeySubjectPublicKeyInfoBase64().isBlank()
                || metadata.createdAtUtc() == null
                || metadata.createdAtUtc().equals(Instant.EPOCH)) {
            return false;
        }

        if (requireDerivedKeyId
                && !metadata.keyId().equals(MasterNetworkIdentityResolver.deriveKeyId(
                        metadata.masterNetworkIdentityId()))) {
            return false;
        }

        try {
            return metadata.publicKeyFingerprint().equals(NetworkIdentityCrypto.publicKeyFingerprintBase64(
                    metadata.publicKeySubjectPublicKeyInfoBase64()));
        } catch (IllegalArgumentException exception) {
            return false;
        }
    }
}
