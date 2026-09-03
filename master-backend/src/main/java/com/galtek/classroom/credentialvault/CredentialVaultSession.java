package com.galtek.classroom.credentialvault;

import java.time.Instant;

public final class CredentialVaultSession {

    private final String token;
    private final Instant createdAtUtc;
    private final Instant expiresAfterLastAccess;

    CredentialVaultSession(String token, Instant createdAtUtc, Instant expiresAfterLastAccess) {
        this.token = token;
        this.createdAtUtc = createdAtUtc;
        this.expiresAfterLastAccess = expiresAfterLastAccess;
    }

    public String token() {
        return token;
    }

    public Instant createdAtUtc() {
        return createdAtUtc;
    }

    public Instant expiresAfterLastAccess() {
        return expiresAfterLastAccess;
    }

    @Override
    public String toString() {
        return "CredentialVaultSession{"
                + "token=<redacted>"
                + ", createdAtUtc=" + createdAtUtc
                + ", expiresAfterLastAccess=" + expiresAfterLastAccess
                + '}';
    }
}
