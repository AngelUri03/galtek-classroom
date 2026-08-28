package com.galtek.classroom.network;

import java.time.Instant;
import java.util.UUID;

public record KnownMasterClient(
        PairingStatus status,
        UUID clientNetworkIdentityId,
        UUID clientInstallationId,
        String clientPublicKeyFingerprint,
        String clientPublicKeySubjectPublicKeyInfoBase64,
        Instant pairedAtUtc,
        Instant revokedAtUtc) {

    public ClientNetworkIdentityDescriptor descriptor() {
        return new ClientNetworkIdentityDescriptor(
                clientNetworkIdentityId,
                clientInstallationId,
                clientPublicKeyFingerprint,
                clientPublicKeySubjectPublicKeyInfoBase64);
    }
}
