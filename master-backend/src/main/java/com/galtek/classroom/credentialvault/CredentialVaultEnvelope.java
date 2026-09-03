package com.galtek.classroom.credentialvault;

record CredentialVaultEnvelope(
        int schemaVersion,
        int cryptoVersion,
        KdfMetadata kdf,
        EncryptedBlob wrappedKey,
        EncryptedBlob vault) {

    CredentialVaultEnvelope withWrappedKey(KdfMetadata nextKdf, EncryptedBlob nextWrappedKey) {
        return new CredentialVaultEnvelope(schemaVersion, cryptoVersion, nextKdf, nextWrappedKey, vault);
    }

    CredentialVaultEnvelope withVault(EncryptedBlob nextVault) {
        return new CredentialVaultEnvelope(schemaVersion, cryptoVersion, kdf, wrappedKey, nextVault);
    }
}

record KdfMetadata(
        String algorithm,
        String salt,
        int iterations) {
}

record EncryptedBlob(
        String nonce,
        String ciphertext) {
}
