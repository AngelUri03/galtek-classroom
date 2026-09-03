package com.galtek.classroom.credentialvault;

import java.time.Instant;

public record CredentialVaultEntryMetadata(
        String credentialId,
        CredentialType credentialType,
        String displayName,
        String loginIdentifier,
        Instant createdAtUtc,
        Instant updatedAtUtc) {
}
