package com.galtek.classroom.network;

import java.util.UUID;

public record ClientNetworkIdentityDescriptor(
        UUID clientNetworkIdentityId,
        UUID clientInstallationId,
        String publicKeyFingerprint,
        String subjectPublicKeyInfoBase64) {
}
