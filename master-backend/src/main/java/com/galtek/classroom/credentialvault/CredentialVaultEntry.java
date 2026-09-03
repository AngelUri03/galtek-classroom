package com.galtek.classroom.credentialvault;

import com.fasterxml.jackson.annotation.JsonCreator;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.time.Instant;
import java.util.Objects;

public final class CredentialVaultEntry {

    private final String credentialId;
    private final CredentialType credentialType;
    private final String displayName;
    private final String loginIdentifier;
    private final String password;
    private final Instant createdAtUtc;
    private final Instant updatedAtUtc;

    @JsonCreator
    public CredentialVaultEntry(
            @JsonProperty("credentialId")
            String credentialId,
            @JsonProperty("credentialType")
            CredentialType credentialType,
            @JsonProperty("displayName")
            String displayName,
            @JsonProperty("loginIdentifier")
            String loginIdentifier,
            @JsonProperty("password")
            String password,
            @JsonProperty("createdAtUtc")
            Instant createdAtUtc,
            @JsonProperty("updatedAtUtc")
            Instant updatedAtUtc) {
        this.credentialId = credentialId;
        this.credentialType = credentialType;
        this.displayName = displayName;
        this.loginIdentifier = loginIdentifier;
        this.password = password;
        this.createdAtUtc = createdAtUtc;
        this.updatedAtUtc = updatedAtUtc;
    }

    @JsonProperty("credentialId")
    public String credentialId() {
        return credentialId;
    }

    @JsonProperty("credentialType")
    public CredentialType credentialType() {
        return credentialType;
    }

    @JsonProperty("displayName")
    public String displayName() {
        return displayName;
    }

    @JsonProperty("loginIdentifier")
    public String loginIdentifier() {
        return loginIdentifier;
    }

    @JsonProperty("password")
    public String password() {
        return password;
    }

    @JsonProperty("createdAtUtc")
    public Instant createdAtUtc() {
        return createdAtUtc;
    }

    @JsonProperty("updatedAtUtc")
    public Instant updatedAtUtc() {
        return updatedAtUtc;
    }

    public CredentialVaultEntry withUpdatedSecret(
            String displayName,
            String loginIdentifier,
            String password,
            Instant updatedAtUtc) {
        return new CredentialVaultEntry(
                credentialId,
                credentialType,
                displayName,
                loginIdentifier,
                password,
                createdAtUtc,
                updatedAtUtc);
    }

    @Override
    public boolean equals(Object other) {
        if (this == other) {
            return true;
        }
        if (!(other instanceof CredentialVaultEntry that)) {
            return false;
        }
        return Objects.equals(credentialId, that.credentialId)
                && credentialType == that.credentialType
                && Objects.equals(displayName, that.displayName)
                && Objects.equals(loginIdentifier, that.loginIdentifier)
                && Objects.equals(password, that.password)
                && Objects.equals(createdAtUtc, that.createdAtUtc)
                && Objects.equals(updatedAtUtc, that.updatedAtUtc);
    }

    @Override
    public int hashCode() {
        return Objects.hash(credentialId, credentialType, displayName, loginIdentifier, password, createdAtUtc,
                updatedAtUtc);
    }

    @Override
    public String toString() {
        return "CredentialVaultEntry{"
                + "credentialId='" + credentialId + '\''
                + ", credentialType=" + credentialType
                + ", displayName='" + displayName + '\''
                + ", loginIdentifier='" + loginIdentifier + '\''
                + ", password=<redacted>"
                + ", createdAtUtc=" + createdAtUtc
                + ", updatedAtUtc=" + updatedAtUtc
                + '}';
    }
}
