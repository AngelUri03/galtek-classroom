package com.galtek.classroom.credentialvault;

import com.galtek.classroom.operations.ErrorCode;
import java.time.Instant;
import java.util.HashSet;

final class CredentialVaultValidator {

    private CredentialVaultValidator() {
    }

    static void validateMasterPassword(String masterPassword) {
        if (masterPassword == null || masterPassword.isBlank()
                || masterPassword.length() > CredentialVaultLimits.MASTER_PASSWORD_MAX_CHARS) {
            throw validation("Credential vault master password is invalid.");
        }
    }

    static CredentialVaultEntryDraft validateDraft(CredentialVaultEntryDraft draft) {
        if (draft == null) {
            throw validation("Credential vault entry is required.");
        }
        validateCredentialType(draft.credentialType());
        validateNonBlankMax(draft.displayName(), CredentialVaultLimits.DISPLAY_NAME_MAX_CHARS);
        validateNonBlankMax(draft.loginIdentifier(), CredentialVaultLimits.LOGIN_IDENTIFIER_MAX_CHARS);
        validatePassword(draft.password());
        return draft;
    }

    static CredentialVaultEntryUpdate validateUpdate(CredentialVaultEntryUpdate update) {
        if (update == null) {
            throw validation("Credential vault entry update is required.");
        }
        validateNonBlankMax(update.displayName(), CredentialVaultLimits.DISPLAY_NAME_MAX_CHARS);
        validateNonBlankMax(update.loginIdentifier(), CredentialVaultLimits.LOGIN_IDENTIFIER_MAX_CHARS);
        validatePassword(update.password());
        return update;
    }

    static void validateCredentialId(String credentialId) {
        validateNonBlankMax(credentialId, CredentialVaultLimits.CREDENTIAL_ID_MAX_CHARS);
    }

    static void validateDocument(CredentialVaultDocument document) {
        if (document == null || document.schemaVersion() != CredentialVaultCrypto.SCHEMA_VERSION
                || document.entries() == null) {
            throw invalid("Credential vault is invalid.");
        }

        HashSet<String> ids = new HashSet<>();
        for (CredentialVaultEntry entry : document.entries()) {
            validateStoredEntry(entry);
            if (!ids.add(entry.credentialId())) {
                throw invalid("Credential vault is invalid.");
            }
        }
    }

    private static void validateStoredEntry(CredentialVaultEntry entry) {
        if (entry == null) {
            throw invalid("Credential vault is invalid.");
        }
        validateCredentialIdForDocument(entry.credentialId());
        validateCredentialTypeForDocument(entry.credentialType());
        validateNonBlankMaxForDocument(entry.displayName(), CredentialVaultLimits.DISPLAY_NAME_MAX_CHARS);
        validateNonBlankMaxForDocument(entry.loginIdentifier(), CredentialVaultLimits.LOGIN_IDENTIFIER_MAX_CHARS);
        validatePasswordForDocument(entry.password());
        if (entry.createdAtUtc() == null || entry.updatedAtUtc() == null) {
            throw invalid("Credential vault is invalid.");
        }
        Instant created = entry.createdAtUtc();
        Instant updated = entry.updatedAtUtc();
        if (updated.isBefore(created)) {
            throw invalid("Credential vault is invalid.");
        }
    }

    private static void validateCredentialType(CredentialType credentialType) {
        if (credentialType == null) {
            throw validation("Credential type is required.");
        }
    }

    private static void validateCredentialTypeForDocument(CredentialType credentialType) {
        if (credentialType == null) {
            throw invalid("Credential vault is invalid.");
        }
    }

    private static void validateCredentialIdForDocument(String value) {
        validateNonBlankMaxForDocument(value, CredentialVaultLimits.CREDENTIAL_ID_MAX_CHARS);
    }

    private static void validatePassword(String password) {
        if (password == null || password.length() > CredentialVaultLimits.CREDENTIAL_PASSWORD_MAX_CHARS) {
            throw validation("Credential password is invalid.");
        }
    }

    private static void validatePasswordForDocument(String password) {
        if (password == null || password.length() > CredentialVaultLimits.CREDENTIAL_PASSWORD_MAX_CHARS) {
            throw invalid("Credential vault is invalid.");
        }
    }

    private static void validateNonBlankMax(String value, int maxLength) {
        if (value == null || value.isBlank() || value.length() > maxLength) {
            throw validation("Credential vault field is invalid.");
        }
    }

    private static void validateNonBlankMaxForDocument(String value, int maxLength) {
        if (value == null || value.isBlank() || value.length() > maxLength) {
            throw invalid("Credential vault is invalid.");
        }
    }

    private static CredentialVaultException validation(String message) {
        return new CredentialVaultException(ErrorCode.INVALID_REQUEST, message);
    }

    private static CredentialVaultException invalid(String message) {
        return new CredentialVaultException(ErrorCode.CREDENTIAL_VAULT_INVALID, message);
    }
}
