package com.galtek.classroom.credentialvault;

import com.galtek.classroom.operations.ErrorCode;
import java.security.SecureRandom;
import java.time.Clock;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Optional;
import java.util.Base64;

public class CredentialVaultService {

    private static final int CREDENTIAL_ID_BYTES = 18;

    private final CredentialVaultStore store;
    private final CredentialVaultCrypto crypto;
    private final CredentialVaultSessionManager sessionManager;
    private final CredentialVaultAuditSink auditSink;
    private final Clock clock;
    private final SecureRandom secureRandom;

    public CredentialVaultService(
            CredentialVaultStore store,
            CredentialVaultCrypto crypto,
            CredentialVaultSessionManager sessionManager,
            CredentialVaultAuditSink auditSink,
            Clock clock,
            SecureRandom secureRandom) {
        this.store = store;
        this.crypto = crypto;
        this.sessionManager = sessionManager;
        this.auditSink = auditSink;
        this.clock = clock;
        this.secureRandom = secureRandom;
    }

    public synchronized void initialize(String masterPassword) {
        CredentialVaultValidator.validateMasterPassword(masterPassword);
        if (store.exists()) {
            throw new CredentialVaultException(
                    ErrorCode.CREDENTIAL_VAULT_ALREADY_INITIALIZED,
                    "Credential vault is already initialized.");
        }

        char[] passwordChars = masterPassword.toCharArray();
        try {
            CredentialVaultDocument document = new CredentialVaultDocument(
                    CredentialVaultCrypto.SCHEMA_VERSION,
                    List.of());
            store.writeNew(crypto.createEnvelope(passwordChars, document));
            auditSink.vaultInitialized();
        } finally {
            Arrays.fill(passwordChars, '\0');
        }
    }

    public synchronized CredentialVaultSession unlock(String masterPassword) {
        CredentialVaultValidator.validateMasterPassword(masterPassword);
        char[] passwordChars = masterPassword.toCharArray();
        CredentialVaultCrypto.UnlockResult unlocked = null;
        try {
            CredentialVaultEnvelope envelope = store.readEnvelope();
            unlocked = crypto.unlock(envelope, passwordChars);
            CredentialVaultSession session = sessionManager.create(unlocked.dek(), unlocked.document(), envelope);
            auditSink.vaultUnlocked();
            return session;
        } finally {
            Arrays.fill(passwordChars, '\0');
            if (unlocked != null) {
                Arrays.fill(unlocked.dek(), (byte) 0);
            }
        }
    }

    public synchronized void lock(String sessionToken) {
        boolean wasUnlocked = sessionManager.hasActiveSession();
        sessionManager.lock(sessionToken);
        if (wasUnlocked) {
            auditSink.vaultLocked();
        }
    }

    public synchronized List<CredentialVaultEntryMetadata> list(String sessionToken) {
        CredentialVaultSessionManager.ActiveVaultSession session = sessionManager.requireActive(sessionToken);
        return session.document().entries().stream()
                .map(CredentialVaultService::metadata)
                .toList();
    }

    public synchronized String reveal(String sessionToken, String credentialId) {
        CredentialVaultValidator.validateCredentialId(credentialId);
        CredentialVaultSessionManager.ActiveVaultSession session = sessionManager.requireActive(sessionToken);
        CredentialVaultEntry entry = findEntry(session.document(), credentialId)
                .orElseThrow(CredentialVaultService::notFound);
        auditSink.credentialRevealed(credentialId);
        return entry.password();
    }

    synchronized CredentialVaultEntry readInternal(String sessionToken, String credentialId) {
        CredentialVaultValidator.validateCredentialId(credentialId);
        CredentialVaultSessionManager.ActiveVaultSession session = sessionManager.requireActive(sessionToken);
        return findEntry(session.document(), credentialId)
                .orElseThrow(CredentialVaultService::notFound);
    }

    public synchronized CredentialVaultEntryMetadata add(String sessionToken, CredentialVaultEntryDraft draft) {
        CredentialVaultValidator.validateDraft(draft);
        CredentialVaultSessionManager.ActiveVaultSession session = sessionManager.requireActive(sessionToken);
        Instant now = clock.instant();
        CredentialVaultEntry entry = new CredentialVaultEntry(
                newCredentialId(),
                draft.credentialType(),
                draft.displayName(),
                draft.loginIdentifier(),
                draft.password(),
                now,
                now);
        List<CredentialVaultEntry> entries = new ArrayList<>(session.document().entries());
        entries.add(entry);
        persistDocument(session, new CredentialVaultDocument(CredentialVaultCrypto.SCHEMA_VERSION, List.copyOf(entries)));
        auditSink.credentialAdded(entry.credentialId());
        return metadata(entry);
    }

    public synchronized CredentialVaultEntryMetadata update(
            String sessionToken,
            String credentialId,
            CredentialVaultEntryUpdate update) {
        CredentialVaultValidator.validateCredentialId(credentialId);
        CredentialVaultValidator.validateUpdate(update);
        CredentialVaultSessionManager.ActiveVaultSession session = sessionManager.requireActive(sessionToken);
        Instant now = clock.instant();
        List<CredentialVaultEntry> entries = session.document().entries();
        ArrayList<CredentialVaultEntry> nextEntries = new ArrayList<>(entries.size());
        CredentialVaultEntry updated = null;
        for (CredentialVaultEntry entry : entries) {
            if (entry.credentialId().equals(credentialId)) {
                updated = entry.withUpdatedSecret(
                        update.displayName(),
                        update.loginIdentifier(),
                        update.password(),
                        now);
                nextEntries.add(updated);
            } else {
                nextEntries.add(entry);
            }
        }
        if (updated == null) {
            throw notFound();
        }
        persistDocument(session, new CredentialVaultDocument(
                CredentialVaultCrypto.SCHEMA_VERSION,
                List.copyOf(nextEntries)));
        auditSink.credentialUpdated(credentialId);
        return metadata(updated);
    }

    public synchronized void remove(String sessionToken, String credentialId) {
        CredentialVaultValidator.validateCredentialId(credentialId);
        CredentialVaultSessionManager.ActiveVaultSession session = sessionManager.requireActive(sessionToken);
        List<CredentialVaultEntry> entries = session.document().entries();
        ArrayList<CredentialVaultEntry> nextEntries = new ArrayList<>(entries.size());
        boolean removed = false;
        for (CredentialVaultEntry entry : entries) {
            if (entry.credentialId().equals(credentialId)) {
                removed = true;
            } else {
                nextEntries.add(entry);
            }
        }
        if (!removed) {
            throw notFound();
        }
        persistDocument(session, new CredentialVaultDocument(
                CredentialVaultCrypto.SCHEMA_VERSION,
                List.copyOf(nextEntries)));
        auditSink.credentialRemoved(credentialId);
    }

    public synchronized void changeMasterPassword(String sessionToken, String newMasterPassword) {
        CredentialVaultValidator.validateMasterPassword(newMasterPassword);
        CredentialVaultSessionManager.ActiveVaultSession session = sessionManager.requireActive(sessionToken);
        char[] passwordChars = newMasterPassword.toCharArray();
        try {
            CredentialVaultCrypto.WrappedKeyEnvelopeParts parts = crypto.rewrapDek(session.dek(), passwordChars);
            CredentialVaultEnvelope nextEnvelope = session.envelope().withWrappedKey(parts.kdf(), parts.wrappedKey());
            store.replace(nextEnvelope);
            session.updateEnvelope(nextEnvelope);
            sessionManager.invalidateAll();
            auditSink.vaultLocked();
        } finally {
            Arrays.fill(passwordChars, '\0');
        }
    }

    private void persistDocument(
            CredentialVaultSessionManager.ActiveVaultSession session,
            CredentialVaultDocument document) {
        CredentialVaultValidator.validateDocument(document);
        CredentialVaultEnvelope nextEnvelope = session.envelope().withVault(crypto.encryptVault(session.dek(), document));
        store.replace(nextEnvelope);
        session.update(document, nextEnvelope);
    }

    private String newCredentialId() {
        byte[] bytes = new byte[CREDENTIAL_ID_BYTES];
        secureRandom.nextBytes(bytes);
        return "cred_" + Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }

    private static Optional<CredentialVaultEntry> findEntry(CredentialVaultDocument document, String credentialId) {
        return document.entries().stream()
                .filter(entry -> entry.credentialId().equals(credentialId))
                .findFirst();
    }

    private static CredentialVaultEntryMetadata metadata(CredentialVaultEntry entry) {
        return new CredentialVaultEntryMetadata(
                entry.credentialId(),
                entry.credentialType(),
                entry.displayName(),
                entry.loginIdentifier(),
                entry.createdAtUtc(),
                entry.updatedAtUtc());
    }

    private static CredentialVaultException notFound() {
        return new CredentialVaultException(ErrorCode.CREDENTIAL_NOT_FOUND, "Credential was not found.");
    }
}
