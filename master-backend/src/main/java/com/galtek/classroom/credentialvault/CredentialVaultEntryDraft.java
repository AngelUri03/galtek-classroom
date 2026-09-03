package com.galtek.classroom.credentialvault;

public record CredentialVaultEntryDraft(
        CredentialType credentialType,
        String displayName,
        String loginIdentifier,
        String password) {
}
