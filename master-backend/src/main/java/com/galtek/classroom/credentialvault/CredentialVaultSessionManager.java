package com.galtek.classroom.credentialvault;

import com.galtek.classroom.operations.ErrorCode;
import java.security.SecureRandom;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Arrays;
import java.util.Base64;

public class CredentialVaultSessionManager {

    private static final int TOKEN_BYTES = 32;

    private final Clock clock;
    private final Duration idleTimeout;
    private final SecureRandom secureRandom;
    private ActiveVaultSession activeSession;

    public CredentialVaultSessionManager() {
        this(Clock.systemUTC(), CredentialVaultCryptoParameters.DEFAULT_SESSION_IDLE_TIMEOUT, new SecureRandom());
    }

    public CredentialVaultSessionManager(Clock clock, Duration idleTimeout, SecureRandom secureRandom) {
        if (clock == null || idleTimeout == null || idleTimeout.isZero() || idleTimeout.isNegative()
                || secureRandom == null) {
            throw new IllegalArgumentException("Credential vault session dependencies are required.");
        }
        this.clock = clock;
        this.idleTimeout = idleTimeout;
        this.secureRandom = secureRandom;
    }

    public synchronized CredentialVaultSession create(
            byte[] dek,
            CredentialVaultDocument document,
            CredentialVaultEnvelope envelope) {
        invalidateActive();
        Instant now = clock.instant();
        String token = randomToken();
        activeSession = new ActiveVaultSession(token, now, now, dek.clone(), document, envelope);
        return new CredentialVaultSession(token, now, now.plus(idleTimeout));
    }

    synchronized ActiveVaultSession requireActive(String token) {
        if (activeSession == null || token == null || !activeSession.token().equals(token)) {
            throw locked();
        }

        Instant now = clock.instant();
        if (Duration.between(activeSession.lastAccessUtc(), now).compareTo(idleTimeout) > 0) {
            invalidateActive();
            throw locked();
        }

        activeSession.touch(now);
        return activeSession;
    }

    public synchronized void lock(String token) {
        if (activeSession == null) {
            return;
        }
        if (token == null || !activeSession.token().equals(token)) {
            throw locked();
        }
        invalidateActive();
    }

    public synchronized void invalidateAll() {
        invalidateActive();
    }

    public synchronized boolean hasActiveSession() {
        return activeSession != null;
    }

    private void invalidateActive() {
        if (activeSession != null) {
            activeSession.destroy();
            activeSession = null;
        }
    }

    private String randomToken() {
        byte[] bytes = new byte[TOKEN_BYTES];
        secureRandom.nextBytes(bytes);
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }

    private CredentialVaultException locked() {
        return new CredentialVaultException(ErrorCode.CREDENTIAL_VAULT_LOCKED, "Credential vault is locked.");
    }

    static final class ActiveVaultSession {

        private final String token;
        private final Instant createdAtUtc;
        private Instant lastAccessUtc;
        private byte[] dek;
        private CredentialVaultDocument document;
        private CredentialVaultEnvelope envelope;

        private ActiveVaultSession(
                String token,
                Instant createdAtUtc,
                Instant lastAccessUtc,
                byte[] dek,
                CredentialVaultDocument document,
                CredentialVaultEnvelope envelope) {
            this.token = token;
            this.createdAtUtc = createdAtUtc;
            this.lastAccessUtc = lastAccessUtc;
            this.dek = dek;
            this.document = document;
            this.envelope = envelope;
        }

        String token() {
            return token;
        }

        Instant createdAtUtc() {
            return createdAtUtc;
        }

        Instant lastAccessUtc() {
            return lastAccessUtc;
        }

        byte[] dek() {
            return dek;
        }

        CredentialVaultDocument document() {
            return document;
        }

        CredentialVaultEnvelope envelope() {
            return envelope;
        }

        void update(CredentialVaultDocument nextDocument, CredentialVaultEnvelope nextEnvelope) {
            this.document = nextDocument;
            this.envelope = nextEnvelope;
        }

        void updateEnvelope(CredentialVaultEnvelope nextEnvelope) {
            this.envelope = nextEnvelope;
        }

        void touch(Instant now) {
            this.lastAccessUtc = now;
        }

        void destroy() {
            if (dek != null) {
                Arrays.fill(dek, (byte) 0);
                dek = null;
            }
            document = null;
            envelope = null;
        }
    }
}
