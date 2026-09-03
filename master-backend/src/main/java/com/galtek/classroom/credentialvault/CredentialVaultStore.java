package com.galtek.classroom.credentialvault;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.fasterxml.jackson.databind.json.JsonMapper;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.AtomicFiles;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

public class CredentialVaultStore {

    public static final String FILE_NAME = "credential-vault.dat";

    private static final JsonMapper OBJECT_MAPPER = JsonMapper.builder()
            .findAndAddModules()
            .disable(SerializationFeature.WRITE_DATES_AS_TIMESTAMPS)
            .build();

    private final Path dataDirectory;
    private final Path filePath;
    private final CredentialVaultFileSecurity fileSecurity;

    public CredentialVaultStore(Path dataDirectory) {
        this(dataDirectory, new LocalCredentialVaultFileSecurity());
    }

    public CredentialVaultStore(Path dataDirectory, CredentialVaultFileSecurity fileSecurity) {
        this.dataDirectory = dataDirectory.toAbsolutePath().normalize();
        this.filePath = this.dataDirectory.resolve(FILE_NAME);
        this.fileSecurity = fileSecurity;
    }

    public Path filePath() {
        return filePath;
    }

    public boolean exists() {
        return Files.exists(filePath);
    }

    public CredentialVaultEnvelope readEnvelope() {
        if (!Files.exists(filePath)) {
            throw new CredentialVaultException(
                    ErrorCode.CREDENTIAL_VAULT_NOT_INITIALIZED,
                    "Credential vault is not initialized.");
        }

        try {
            CredentialVaultEnvelope envelope = OBJECT_MAPPER.readValue(filePath.toFile(), CredentialVaultEnvelope.class);
            validateEnvelopeShape(envelope);
            return envelope;
        } catch (CredentialVaultException exception) {
            throw exception;
        } catch (IOException | RuntimeException exception) {
            throw invalid(exception);
        }
    }

    public void writeNew(CredentialVaultEnvelope envelope) {
        validateEnvelopeShape(envelope);
        if (Files.exists(filePath)) {
            throw new CredentialVaultException(
                    ErrorCode.CREDENTIAL_VAULT_ALREADY_INITIALIZED,
                    "Credential vault is already initialized.");
        }
        write(envelope, false);
    }

    public void replace(CredentialVaultEnvelope envelope) {
        validateEnvelopeShape(envelope);
        if (!Files.exists(filePath)) {
            throw new CredentialVaultException(
                    ErrorCode.CREDENTIAL_VAULT_NOT_INITIALIZED,
                    "Credential vault is not initialized.");
        }
        write(envelope, true);
    }

    private void write(CredentialVaultEnvelope envelope, boolean replaceExisting) {
        try {
            Files.createDirectories(dataDirectory);
            AtomicFiles.writeAtomically(
                    filePath,
                    replaceExisting,
                    output -> OBJECT_MAPPER.writerWithDefaultPrettyPrinter().writeValue(output, envelope));
            fileSecurity.protect(filePath);
        } catch (JsonProcessingException exception) {
            throw new CredentialVaultException(
                    ErrorCode.CREDENTIAL_VAULT_INVALID,
                    "Credential vault could not be serialized.",
                    exception);
        } catch (IOException exception) {
            throw new CredentialVaultException(
                    ErrorCode.MASTER_STORAGE_FULL,
                    "Credential vault could not be persisted.",
                    exception);
        }
    }

    private void validateEnvelopeShape(CredentialVaultEnvelope envelope) {
        if (envelope == null
                || envelope.schemaVersion() != CredentialVaultCrypto.SCHEMA_VERSION
                || envelope.cryptoVersion() != CredentialVaultCrypto.CRYPTO_VERSION
                || envelope.kdf() == null
                || envelope.wrappedKey() == null
                || envelope.vault() == null) {
            throw new CredentialVaultException(
                    ErrorCode.CREDENTIAL_VAULT_INVALID,
                    "Credential vault is invalid.");
        }
    }

    private CredentialVaultException invalid(Throwable cause) {
        return new CredentialVaultException(
                ErrorCode.CREDENTIAL_VAULT_INVALID,
                "Credential vault is invalid.",
                cause);
    }
}
