package com.galtek.classroom.credentialvault;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.fasterxml.jackson.databind.json.JsonMapper;
import com.galtek.classroom.operations.ErrorCode;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.SecureRandom;
import java.security.spec.KeySpec;
import java.util.Arrays;
import java.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.PBEKeySpec;
import javax.crypto.spec.SecretKeySpec;

public class CredentialVaultCrypto {

    static final int SCHEMA_VERSION = 1;
    static final int CRYPTO_VERSION = 1;
    static final String KDF_ALGORITHM = "PBKDF2-HMAC-SHA256";

    private static final String JAVA_KDF_ALGORITHM = "PBKDF2WithHmacSHA256";
    private static final String AES_GCM_ALGORITHM = "AES/GCM/NoPadding";
    private static final int KEY_BITS = 256;
    private static final int KEY_BYTES = 32;
    private static final int SALT_BYTES = 16;
    private static final int GCM_NONCE_BYTES = 12;
    private static final int GCM_TAG_BITS = 128;
    private static final byte[] WRAPPED_KEY_AAD =
            "galtek-classroom:credential-vault:wrapped-key:v1".getBytes(StandardCharsets.UTF_8);
    private static final byte[] VAULT_AAD =
            "galtek-classroom:credential-vault:vault:v1".getBytes(StandardCharsets.UTF_8);

    private static final JsonMapper OBJECT_MAPPER = JsonMapper.builder()
            .findAndAddModules()
            .disable(SerializationFeature.WRITE_DATES_AS_TIMESTAMPS)
            .build();

    private final SecureRandom secureRandom;
    private final CredentialVaultCryptoParameters parameters;

    public CredentialVaultCrypto() {
        this(CredentialVaultCryptoParameters.productionDefaults(), new SecureRandom());
    }

    public CredentialVaultCrypto(CredentialVaultCryptoParameters parameters, SecureRandom secureRandom) {
        this.parameters = parameters;
        this.secureRandom = secureRandom;
    }

    CredentialVaultEnvelope createEnvelope(char[] masterPassword, CredentialVaultDocument document) {
        byte[] dek = randomBytes(KEY_BYTES);
        try {
            byte[] salt = randomBytes(SALT_BYTES);
            byte[] kek = deriveKey(masterPassword, salt, parameters.pbkdf2Iterations());
            try {
                EncryptedBlob wrappedKey = encrypt(kek, dek, WRAPPED_KEY_AAD);
                EncryptedBlob vault = encrypt(dek, serialize(document), VAULT_AAD);
                return new CredentialVaultEnvelope(
                        SCHEMA_VERSION,
                        CRYPTO_VERSION,
                        new KdfMetadata(KDF_ALGORITHM, b64(salt), parameters.pbkdf2Iterations()),
                        wrappedKey,
                        vault);
            } finally {
                Arrays.fill(kek, (byte) 0);
            }
        } finally {
            Arrays.fill(dek, (byte) 0);
        }
    }

    UnlockResult unlock(CredentialVaultEnvelope envelope, char[] masterPassword) {
        validateEnvelope(envelope);
        byte[] salt = decode(envelope.kdf().salt(), "salt");
        if (salt.length != SALT_BYTES) {
            throw invalid("Credential vault is invalid.");
        }
        byte[] kek = deriveKey(masterPassword, salt, envelope.kdf().iterations());
        try {
            byte[] dek;
            try {
                dek = decrypt(kek, envelope.wrappedKey(), WRAPPED_KEY_AAD);
            } catch (GeneralSecurityException exception) {
                throw new CredentialVaultException(
                        ErrorCode.CREDENTIAL_VAULT_UNLOCK_FAILED,
                        "Credential vault could not be unlocked.",
                        exception);
            }
            try {
                if (dek.length != KEY_BYTES) {
                    throw invalid("Credential vault is invalid.");
                }
                CredentialVaultDocument document;
                try {
                    document = deserialize(decrypt(dek, envelope.vault(), VAULT_AAD));
                } catch (GeneralSecurityException exception) {
                    throw invalid("Credential vault is invalid.", exception);
                }
                CredentialVaultValidator.validateDocument(document);
                return new UnlockResult(dek, document);
            } catch (RuntimeException exception) {
                Arrays.fill(dek, (byte) 0);
                throw exception;
            }
        } finally {
            Arrays.fill(kek, (byte) 0);
        }
    }

    EncryptedBlob encryptVault(byte[] dek, CredentialVaultDocument document) {
        if (dek == null || dek.length != KEY_BYTES) {
            throw invalid("Credential vault is locked.");
        }
        return encrypt(dek, serialize(document), VAULT_AAD);
    }

    WrappedKeyEnvelopeParts rewrapDek(byte[] dek, char[] newMasterPassword) {
        byte[] salt = randomBytes(SALT_BYTES);
        byte[] kek = deriveKey(newMasterPassword, salt, parameters.pbkdf2Iterations());
        try {
            return new WrappedKeyEnvelopeParts(
                    new KdfMetadata(KDF_ALGORITHM, b64(salt), parameters.pbkdf2Iterations()),
                    encrypt(kek, dek, WRAPPED_KEY_AAD));
        } finally {
            Arrays.fill(kek, (byte) 0);
        }
    }

    private byte[] deriveKey(char[] password, byte[] salt, int iterations) {
        try {
            KeySpec spec = new PBEKeySpec(password, salt, iterations, KEY_BITS);
            return SecretKeyFactory.getInstance(JAVA_KDF_ALGORITHM)
                    .generateSecret(spec)
                    .getEncoded();
        } catch (GeneralSecurityException exception) {
            throw invalid("Credential vault crypto is unavailable.", exception);
        }
    }

    private EncryptedBlob encrypt(byte[] key, byte[] plaintext, byte[] aad) {
        try {
            byte[] nonce = randomBytes(GCM_NONCE_BYTES);
            Cipher cipher = Cipher.getInstance(AES_GCM_ALGORITHM);
            cipher.init(Cipher.ENCRYPT_MODE, new SecretKeySpec(key, "AES"),
                    new GCMParameterSpec(GCM_TAG_BITS, nonce));
            cipher.updateAAD(aad);
            return new EncryptedBlob(b64(nonce), b64(cipher.doFinal(plaintext)));
        } catch (GeneralSecurityException exception) {
            throw invalid("Credential vault encryption failed.", exception);
        }
    }

    private byte[] decrypt(byte[] key, EncryptedBlob blob, byte[] aad) throws GeneralSecurityException {
        byte[] nonce = decode(blob.nonce(), "nonce");
        byte[] ciphertext = decode(blob.ciphertext(), "ciphertext");
        if (nonce.length != GCM_NONCE_BYTES || ciphertext.length <= 16) {
            throw invalid("Credential vault is invalid.");
        }
        Cipher cipher = Cipher.getInstance(AES_GCM_ALGORITHM);
        cipher.init(Cipher.DECRYPT_MODE, new SecretKeySpec(key, "AES"),
                new GCMParameterSpec(GCM_TAG_BITS, nonce));
        cipher.updateAAD(aad);
        return cipher.doFinal(ciphertext);
    }

    private byte[] serialize(CredentialVaultDocument document) {
        try {
            return OBJECT_MAPPER.writeValueAsBytes(document);
        } catch (JsonProcessingException exception) {
            throw invalid("Credential vault document could not be serialized.", exception);
        }
    }

    private CredentialVaultDocument deserialize(byte[] plaintext) {
        try {
            return OBJECT_MAPPER.readValue(plaintext, CredentialVaultDocument.class);
        } catch (IOException | RuntimeException exception) {
            throw invalid("Credential vault is invalid.", exception);
        }
    }

    private void validateEnvelope(CredentialVaultEnvelope envelope) {
        if (envelope == null
                || envelope.schemaVersion() != SCHEMA_VERSION
                || envelope.cryptoVersion() != CRYPTO_VERSION
                || envelope.kdf() == null
                || !KDF_ALGORITHM.equals(envelope.kdf().algorithm())
                || envelope.kdf().iterations() < 1
                || envelope.wrappedKey() == null
                || envelope.vault() == null) {
            throw invalid("Credential vault is invalid.");
        }
        decode(envelope.kdf().salt(), "salt");
        decode(envelope.wrappedKey().nonce(), "wrapped nonce");
        decode(envelope.wrappedKey().ciphertext(), "wrapped ciphertext");
        decode(envelope.vault().nonce(), "vault nonce");
        decode(envelope.vault().ciphertext(), "vault ciphertext");
    }

    private byte[] randomBytes(int length) {
        byte[] bytes = new byte[length];
        secureRandom.nextBytes(bytes);
        return bytes;
    }

    private String b64(byte[] bytes) {
        return Base64.getEncoder().encodeToString(bytes);
    }

    private byte[] decode(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw invalid("Credential vault is invalid.");
        }
        try {
            return Base64.getDecoder().decode(value);
        } catch (IllegalArgumentException exception) {
            throw invalid("Credential vault is invalid.", exception);
        }
    }

    private CredentialVaultException invalid(String message) {
        return new CredentialVaultException(ErrorCode.CREDENTIAL_VAULT_INVALID, message);
    }

    private CredentialVaultException invalid(String message, Throwable cause) {
        return new CredentialVaultException(ErrorCode.CREDENTIAL_VAULT_INVALID, message, cause);
    }

    record UnlockResult(
            byte[] dek,
            CredentialVaultDocument document) {
    }

    record WrappedKeyEnvelopeParts(
            KdfMetadata kdf,
            EncryptedBlob wrappedKey) {
    }
}
