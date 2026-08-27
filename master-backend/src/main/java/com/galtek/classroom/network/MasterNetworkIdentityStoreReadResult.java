package com.galtek.classroom.network;

public record MasterNetworkIdentityStoreReadResult(
        MasterNetworkIdentityStoreReadStatus status,
        MasterNetworkIdentityMetadata metadata,
        String filePath,
        String errorMessage) {

    public static MasterNetworkIdentityStoreReadResult missing(String filePath) {
        return new MasterNetworkIdentityStoreReadResult(
                MasterNetworkIdentityStoreReadStatus.MISSING,
                null,
                filePath,
                null);
    }

    public static MasterNetworkIdentityStoreReadResult loaded(
            MasterNetworkIdentityMetadata metadata,
            String filePath) {
        return new MasterNetworkIdentityStoreReadResult(
                MasterNetworkIdentityStoreReadStatus.LOADED,
                metadata,
                filePath,
                null);
    }

    public static MasterNetworkIdentityStoreReadResult invalid(String filePath, String errorMessage) {
        return new MasterNetworkIdentityStoreReadResult(
                MasterNetworkIdentityStoreReadStatus.INVALID,
                null,
                filePath,
                errorMessage);
    }
}
