package com.galtek.classroom.network;

public record MasterNetworkKeyCreationResult(
        MasterNetworkKeyCreationStatus status,
        String publicKeyFingerprint,
        String subjectPublicKeyInfoBase64,
        String errorMessage) {

    public boolean created() {
        return status == MasterNetworkKeyCreationStatus.CREATED;
    }

    public static MasterNetworkKeyCreationResult created(
            String publicKeyFingerprint,
            String subjectPublicKeyInfoBase64) {
        return new MasterNetworkKeyCreationResult(
                MasterNetworkKeyCreationStatus.CREATED,
                publicKeyFingerprint,
                subjectPublicKeyInfoBase64,
                null);
    }

    public static MasterNetworkKeyCreationResult alreadyExists() {
        return new MasterNetworkKeyCreationResult(
                MasterNetworkKeyCreationStatus.ALREADY_EXISTS,
                null,
                null,
                "Master Network Identity key material already exists.");
    }

    public static MasterNetworkKeyCreationResult failed(String errorMessage) {
        return new MasterNetworkKeyCreationResult(
                MasterNetworkKeyCreationStatus.FAILED,
                null,
                null,
                errorMessage);
    }
}
