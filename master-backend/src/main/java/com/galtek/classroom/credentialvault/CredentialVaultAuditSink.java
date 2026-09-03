package com.galtek.classroom.credentialvault;

public interface CredentialVaultAuditSink {

    void vaultInitialized();

    void vaultUnlocked();

    void vaultLocked();

    void credentialAdded(String credentialId);

    void credentialUpdated(String credentialId);

    void credentialRemoved(String credentialId);

    void credentialRevealed(String credentialId);

    static CredentialVaultAuditSink noop() {
        return new CredentialVaultAuditSink() {
            @Override
            public void vaultInitialized() {
            }

            @Override
            public void vaultUnlocked() {
            }

            @Override
            public void vaultLocked() {
            }

            @Override
            public void credentialAdded(String credentialId) {
            }

            @Override
            public void credentialUpdated(String credentialId) {
            }

            @Override
            public void credentialRemoved(String credentialId) {
            }

            @Override
            public void credentialRevealed(String credentialId) {
            }
        };
    }
}
