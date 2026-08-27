package com.galtek.classroom.network;

import java.time.Instant;
import java.util.UUID;

public record MasterNetworkIdentityMetadata(
        int schemaVersion,
        UUID masterNetworkIdentityId,
        String keyId,
        String publicKeyFingerprint,
        String publicKeySubjectPublicKeyInfoBase64,
        Instant createdAtUtc) {
}
