package com.galtek.classroom.credentialvault;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class LoggingCredentialVaultAuditSink implements CredentialVaultAuditSink {

    private static final Logger LOGGER = LoggerFactory.getLogger(LoggingCredentialVaultAuditSink.class);

    @Override
    public void vaultInitialized() {
        LOGGER.info("Credential vault initialized");
    }

    @Override
    public void vaultUnlocked() {
        LOGGER.info("Credential vault unlocked");
    }

    @Override
    public void vaultLocked() {
        LOGGER.info("Credential vault locked");
    }

    @Override
    public void credentialAdded(String credentialId) {
        LOGGER.info("Credential vault entry added [{}]", credentialId);
    }

    @Override
    public void credentialUpdated(String credentialId) {
        LOGGER.info("Credential vault entry updated [{}]", credentialId);
    }

    @Override
    public void credentialRemoved(String credentialId) {
        LOGGER.info("Credential vault entry removed [{}]", credentialId);
    }

    @Override
    public void credentialRevealed(String credentialId) {
        LOGGER.info("Credential vault entry revealed [{}]", credentialId);
    }
}
