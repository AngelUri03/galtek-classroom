package com.galtek.classroom.credentialvault;

import java.time.Duration;

public record CredentialVaultCryptoParameters(
        int pbkdf2Iterations,
        Duration sessionIdleTimeout) {

    public static final int DEFAULT_PBKDF2_ITERATIONS = 310_000;
    public static final Duration DEFAULT_SESSION_IDLE_TIMEOUT = Duration.ofMinutes(5);

    public CredentialVaultCryptoParameters {
        if (pbkdf2Iterations < 1) {
            throw new IllegalArgumentException("pbkdf2Iterations must be positive.");
        }
        if (sessionIdleTimeout == null || sessionIdleTimeout.isZero() || sessionIdleTimeout.isNegative()) {
            throw new IllegalArgumentException("sessionIdleTimeout must be positive.");
        }
    }

    public static CredentialVaultCryptoParameters productionDefaults() {
        return new CredentialVaultCryptoParameters(DEFAULT_PBKDF2_ITERATIONS, DEFAULT_SESSION_IDLE_TIMEOUT);
    }

    public static CredentialVaultCryptoParameters testFast() {
        return new CredentialVaultCryptoParameters(2, DEFAULT_SESSION_IDLE_TIMEOUT);
    }
}
