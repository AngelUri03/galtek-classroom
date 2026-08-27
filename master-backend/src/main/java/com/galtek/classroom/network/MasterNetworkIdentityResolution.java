package com.galtek.classroom.network;

public record MasterNetworkIdentityResolution(
        MasterNetworkIdentityStatus status,
        MasterNetworkIdentityMetadata metadata,
        String filePath,
        String errorMessage,
        boolean created) {

    public boolean ready() {
        return status == MasterNetworkIdentityStatus.READY;
    }

    public static MasterNetworkIdentityResolution ready(
            MasterNetworkIdentityMetadata metadata,
            String filePath,
            boolean created) {
        return new MasterNetworkIdentityResolution(
                MasterNetworkIdentityStatus.READY,
                metadata,
                filePath,
                null,
                created);
    }

    public static MasterNetworkIdentityResolution invalid(String filePath, String errorMessage) {
        return new MasterNetworkIdentityResolution(
                MasterNetworkIdentityStatus.INVALID,
                null,
                filePath,
                errorMessage,
                false);
    }

    public static MasterNetworkIdentityResolution keyMissing(
            String filePath,
            MasterNetworkIdentityMetadata metadata,
            String errorMessage) {
        return new MasterNetworkIdentityResolution(
                MasterNetworkIdentityStatus.KEY_MISSING,
                metadata,
                filePath,
                errorMessage,
                false);
    }
}
