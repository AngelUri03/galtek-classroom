package com.galtek.classroom.network;

import java.util.UUID;

public record MasterNetworkConnectionAuthorization(
        boolean accepted,
        PairingStatus status,
        ClientNetworkIdentityDescriptor descriptor,
        String reasonCode,
        String message) {

    public UUID clientNetworkIdentityId() {
        return descriptor == null ? null : descriptor.clientNetworkIdentityId();
    }

    public UUID clientInstallationId() {
        return descriptor == null ? null : descriptor.clientInstallationId();
    }

    public static MasterNetworkConnectionAuthorization accepted(ClientNetworkIdentityDescriptor descriptor) {
        return new MasterNetworkConnectionAuthorization(
                true,
                PairingStatus.PAIRED,
                descriptor,
                null,
                null);
    }

    public static MasterNetworkConnectionAuthorization rejected(
            PairingStatus status,
            ClientNetworkIdentityDescriptor descriptor,
            String reasonCode,
            String message) {
        return new MasterNetworkConnectionAuthorization(
                false,
                status,
                descriptor,
                reasonCode,
                message);
    }
}
