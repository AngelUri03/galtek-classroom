package com.galtek.classroom.network;

public record MasterNetworkKeyLookupResult(
        MasterNetworkKeyLookupStatus status,
        String publicKeyFingerprint,
        String subjectPublicKeyInfoBase64,
        String errorMessage) {

    public static MasterNetworkKeyLookupResult found(
            String publicKeyFingerprint,
            String subjectPublicKeyInfoBase64) {
        return new MasterNetworkKeyLookupResult(
                MasterNetworkKeyLookupStatus.FOUND,
                publicKeyFingerprint,
                subjectPublicKeyInfoBase64,
                null);
    }

    public static MasterNetworkKeyLookupResult missing() {
        return new MasterNetworkKeyLookupResult(
                MasterNetworkKeyLookupStatus.MISSING,
                null,
                null,
                "Master Network Identity key material is missing.");
    }

    public static MasterNetworkKeyLookupResult invalid(String errorMessage) {
        return new MasterNetworkKeyLookupResult(
                MasterNetworkKeyLookupStatus.INVALID,
                null,
                null,
                errorMessage);
    }
}
