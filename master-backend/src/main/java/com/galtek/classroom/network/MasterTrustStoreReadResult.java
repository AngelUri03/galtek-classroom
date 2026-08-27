package com.galtek.classroom.network;

public record MasterTrustStoreReadResult(
        MasterTrustStoreReadStatus status,
        MasterTrustDocument document,
        String filePath,
        String errorMessage) {

    public static MasterTrustStoreReadResult missing(String filePath) {
        return new MasterTrustStoreReadResult(MasterTrustStoreReadStatus.MISSING, null, filePath, null);
    }

    public static MasterTrustStoreReadResult loaded(MasterTrustDocument document, String filePath) {
        return new MasterTrustStoreReadResult(MasterTrustStoreReadStatus.LOADED, document, filePath, null);
    }

    public static MasterTrustStoreReadResult invalid(String filePath, String errorMessage) {
        return new MasterTrustStoreReadResult(MasterTrustStoreReadStatus.INVALID, null, filePath, errorMessage);
    }
}
