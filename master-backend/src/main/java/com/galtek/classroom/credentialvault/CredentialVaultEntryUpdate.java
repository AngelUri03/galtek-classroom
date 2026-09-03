package com.galtek.classroom.credentialvault;

public record CredentialVaultEntryUpdate(
        String displayName,
        String loginIdentifier,
        String password) {
}
