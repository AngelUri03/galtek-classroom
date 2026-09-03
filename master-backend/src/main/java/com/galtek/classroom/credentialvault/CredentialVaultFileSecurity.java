package com.galtek.classroom.credentialvault;

import java.nio.file.Path;

@FunctionalInterface
public interface CredentialVaultFileSecurity {

    void protect(Path path);

    static CredentialVaultFileSecurity noop() {
        return path -> {
        };
    }
}
