package com.galtek.classroom.credentialvault;

import java.util.List;

record CredentialVaultDocument(
        int schemaVersion,
        List<CredentialVaultEntry> entries) {
}
