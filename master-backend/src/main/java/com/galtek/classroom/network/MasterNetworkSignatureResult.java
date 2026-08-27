package com.galtek.classroom.network;

public record MasterNetworkSignatureResult(
        MasterNetworkSignatureStatus status,
        String signatureBase64,
        String errorMessage) {

    public boolean signed() {
        return status == MasterNetworkSignatureStatus.SIGNED;
    }

    public static MasterNetworkSignatureResult signed(String signatureBase64) {
        return new MasterNetworkSignatureResult(
                MasterNetworkSignatureStatus.SIGNED,
                signatureBase64,
                null);
    }

    public static MasterNetworkSignatureResult missing() {
        return new MasterNetworkSignatureResult(
                MasterNetworkSignatureStatus.MISSING,
                null,
                "Master Network Identity key material is missing.");
    }

    public static MasterNetworkSignatureResult invalid(String errorMessage) {
        return new MasterNetworkSignatureResult(
                MasterNetworkSignatureStatus.INVALID,
                null,
                errorMessage);
    }
}
