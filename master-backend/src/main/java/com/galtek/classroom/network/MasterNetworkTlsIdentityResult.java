package com.galtek.classroom.network;

import java.security.PrivateKey;
import java.security.cert.X509Certificate;

public record MasterNetworkTlsIdentityResult(
        MasterNetworkTlsIdentityStatus status,
        String publicKeyFingerprint,
        X509Certificate certificate,
        PrivateKey privateKey,
        String errorMessage) {

    public boolean ready() {
        return status == MasterNetworkTlsIdentityStatus.READY;
    }

    public static MasterNetworkTlsIdentityResult ready(
            String publicKeyFingerprint,
            X509Certificate certificate,
            PrivateKey privateKey) {
        return new MasterNetworkTlsIdentityResult(
                MasterNetworkTlsIdentityStatus.READY,
                publicKeyFingerprint,
                certificate,
                privateKey,
                null);
    }

    public static MasterNetworkTlsIdentityResult missing() {
        return new MasterNetworkTlsIdentityResult(
                MasterNetworkTlsIdentityStatus.MISSING,
                null,
                null,
                null,
                "Master Network Identity key material is missing.");
    }

    public static MasterNetworkTlsIdentityResult invalid(String errorMessage) {
        return new MasterNetworkTlsIdentityResult(
                MasterNetworkTlsIdentityStatus.INVALID,
                null,
                null,
                null,
                errorMessage);
    }
}
