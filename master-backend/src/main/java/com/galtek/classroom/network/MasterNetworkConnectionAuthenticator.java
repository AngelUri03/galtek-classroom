package com.galtek.classroom.network;

import com.galtek.classroom.network.v1.ClientHello;
import java.util.UUID;

public class MasterNetworkConnectionAuthenticator {

    private final MasterPairingService pairingService;

    public MasterNetworkConnectionAuthenticator(MasterPairingService pairingService) {
        this.pairingService = pairingService;
    }

    public MasterNetworkConnectionAuthorization authenticate(
            ClientHello hello,
            String tlsClientPublicKeyFingerprint) {
        if (hello == null) {
            return MasterNetworkConnectionAuthorization.rejected(
                    PairingStatus.UNPAIRED,
                    null,
                    MasterNetworkTransportConstants.PROTOCOL_VIOLATION,
                    "ClientHello is required.");
        }

        ClientNetworkIdentityDescriptor descriptor = descriptorFrom(hello);
        if (descriptor == null) {
            return MasterNetworkConnectionAuthorization.rejected(
                    PairingStatus.UNPAIRED,
                    null,
                    MasterNetworkTransportConstants.NETWORK_IDENTITY_MISMATCH,
                    "ClientHello does not contain a valid Network Identity descriptor.");
        }

        if (!NetworkIdentityCrypto.isValidSha256Hex(tlsClientPublicKeyFingerprint)) {
            return MasterNetworkConnectionAuthorization.rejected(
                    PairingStatus.UNPAIRED,
                    descriptor,
                    MasterNetworkTransportConstants.MUTUAL_TLS_REQUIRED,
                    "mTLS client certificate is required.");
        }

        if (!tlsClientPublicKeyFingerprint.equals(descriptor.publicKeyFingerprint())) {
            return MasterNetworkConnectionAuthorization.rejected(
                    PairingStatus.UNPAIRED,
                    descriptor,
                    MasterNetworkTransportConstants.CERTIFICATE_FINGERPRINT_MISMATCH,
                    "mTLS client certificate does not match Client Network Identity.");
        }

        MasterClientAuthorization authorization = pairingService.isAuthenticatedClientAuthorized(descriptor);
        if (!authorization.authorized()) {
            return MasterNetworkConnectionAuthorization.rejected(
                    authorization.status(),
                    descriptor,
                    authorization.status() == PairingStatus.REVOKED
                            ? MasterNetworkTransportConstants.CLIENT_REVOKED
                            : MasterNetworkTransportConstants.CLIENT_NOT_PAIRED,
                    authorization.message());
        }

        return MasterNetworkConnectionAuthorization.accepted(descriptor);
    }

    public MasterNetworkConnectionAuthorization authorizeExisting(
            ClientNetworkIdentityDescriptor descriptor,
            String tlsClientPublicKeyFingerprint) {
        if (descriptor == null) {
            return MasterNetworkConnectionAuthorization.rejected(
                    PairingStatus.UNPAIRED,
                    null,
                    MasterNetworkTransportConstants.NETWORK_IDENTITY_MISMATCH,
                    "Client Network Identity descriptor is required.");
        }

        if (!NetworkIdentityCrypto.isValidSha256Hex(tlsClientPublicKeyFingerprint)) {
            return MasterNetworkConnectionAuthorization.rejected(
                    PairingStatus.UNPAIRED,
                    descriptor,
                    MasterNetworkTransportConstants.MUTUAL_TLS_REQUIRED,
                    "mTLS client certificate is required.");
        }

        if (!tlsClientPublicKeyFingerprint.equals(descriptor.publicKeyFingerprint())) {
            return MasterNetworkConnectionAuthorization.rejected(
                    PairingStatus.UNPAIRED,
                    descriptor,
                    MasterNetworkTransportConstants.CERTIFICATE_FINGERPRINT_MISMATCH,
                    "mTLS client certificate does not match Client Network Identity.");
        }

        MasterClientAuthorization authorization = pairingService.isAuthenticatedClientAuthorized(descriptor);
        if (!authorization.authorized()) {
            return MasterNetworkConnectionAuthorization.rejected(
                    authorization.status(),
                    descriptor,
                    authorization.status() == PairingStatus.REVOKED
                            ? MasterNetworkTransportConstants.CLIENT_REVOKED
                            : MasterNetworkTransportConstants.CLIENT_NOT_PAIRED,
                    authorization.message());
        }

        return MasterNetworkConnectionAuthorization.accepted(descriptor);
    }

    private static ClientNetworkIdentityDescriptor descriptorFrom(ClientHello hello) {
        try {
            UUID clientNetworkIdentityId = UUID.fromString(hello.getClientNetworkIdentityId());
            UUID clientInstallationId = UUID.fromString(hello.getClientInstallationId());
            if (!NetworkIdentityCrypto.isValidSha256Hex(hello.getClientPublicKeyFingerprint())
                    || hello.getClientPublicKeySubjectPublicKeyInfoBase64().isBlank()) {
                return null;
            }
            NetworkIdentityCrypto.importPublicKey(hello.getClientPublicKeySubjectPublicKeyInfoBase64());
            if (!hello.getClientPublicKeyFingerprint().equals(NetworkIdentityCrypto.publicKeyFingerprintBase64(
                    hello.getClientPublicKeySubjectPublicKeyInfoBase64()))) {
                return null;
            }
            return new ClientNetworkIdentityDescriptor(
                    clientNetworkIdentityId,
                    clientInstallationId,
                    hello.getClientPublicKeyFingerprint(),
                    hello.getClientPublicKeySubjectPublicKeyInfoBase64());
        } catch (Exception exception) {
            return null;
        }
    }
}
