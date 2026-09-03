package com.galtek.classroom.credentialvault;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.galtek.classroom.operations.ErrorCode;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.SecureRandom;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;
import java.util.Timer;
import java.util.concurrent.ScheduledExecutorService;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class CredentialVaultServiceTest {

    private static final ObjectMapper OBJECT_MAPPER = new ObjectMapper();
    private static final String MASTER_PASSWORD = "maestra vault password";
    private static final String NEW_MASTER_PASSWORD = "new maestra vault password";

    @TempDir
    Path tempDir;
    private int fixtureNumber;

    @Test
    void absentVaultIsNotInitializedAndInitializeCreatesVaultWithoutPlaintextSecrets() throws Exception {
        TestVault fixture = newVault();

        assertVaultError(
                () -> fixture.store().readEnvelope(),
                ErrorCode.CREDENTIAL_VAULT_NOT_INITIALIZED);

        fixture.service().initialize(MASTER_PASSWORD);
        CredentialVaultSession session = fixture.service().unlock(MASTER_PASSWORD);
        fixture.service().add(session.token(), new CredentialVaultEntryDraft(
                CredentialType.WINDOWS_ACCOUNT,
                "PC01 Windows",
                "LAB\\pc01",
                "P a s s w 0 r d ! ñ"));

        String serialized = Files.readString(fixture.store().filePath(), StandardCharsets.UTF_8);
        assertThat(serialized)
                .doesNotContain(MASTER_PASSWORD)
                .doesNotContain("P a s s w 0 r d ! ñ")
                .doesNotContain("LAB\\pc01")
                .doesNotContain("PC01 Windows")
                .doesNotContain("WINDOWS_ACCOUNT");
        assertThat(fixture.fileSecurity().protectedPaths()).contains(fixture.store().filePath());
    }

    @Test
    void initializeTwiceIsRejectedAndRestartRequiresUnlockAgain() {
        TestVault fixture = newVault();
        fixture.service().initialize(MASTER_PASSWORD);

        assertVaultError(
                () -> fixture.service().initialize(MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_ALREADY_INITIALIZED);

        CredentialVaultSession session = fixture.service().unlock(MASTER_PASSWORD);
        TestVault restarted = newVault(fixture.clock(), fixture.auditSink(), fixture.fileSecurity());
        assertVaultError(
                () -> restarted.service().list(session.token()),
                ErrorCode.CREDENTIAL_VAULT_LOCKED);
    }

    @Test
    void unlockUsesMasterPasswordWithoutPersistingTokensAndNewUnlockInvalidatesPreviousSession() throws Exception {
        TestVault fixture = newVault();
        fixture.service().initialize(MASTER_PASSWORD);

        CredentialVaultSession first = fixture.service().unlock(MASTER_PASSWORD);
        assertThat(first.token()).isNotEqualTo(MASTER_PASSWORD);
        assertThat(Files.readString(fixture.store().filePath(), StandardCharsets.UTF_8))
                .doesNotContain(first.token());

        CredentialVaultSession second = fixture.service().unlock(MASTER_PASSWORD);
        assertThat(second.token()).isNotEqualTo(first.token());
        assertVaultError(
                () -> fixture.service().list(first.token()),
                ErrorCode.CREDENTIAL_VAULT_LOCKED);
        assertThat(fixture.service().list(second.token())).isEmpty();
    }

    @Test
    void wrongPasswordAndWrappedDekTamperDoNotLeakSecrets() throws Exception {
        TestVault fixture = newVault();
        fixture.service().initialize(MASTER_PASSWORD);

        assertVaultError(
                () -> fixture.service().unlock("wrong " + MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_UNLOCK_FAILED,
                MASTER_PASSWORD);

        mutateEnvelope(fixture.store().filePath(), "wrappedKey", "ciphertext",
                CredentialVaultServiceTest::flipFirstBase64Byte);
        assertThatThrownBy(() -> fixture.service().unlock(MASTER_PASSWORD))
                .isInstanceOf(CredentialVaultException.class)
                .extracting("errorCode")
                .satisfies(code -> assertThat(code).isIn(
                        ErrorCode.CREDENTIAL_VAULT_UNLOCK_FAILED,
                        ErrorCode.CREDENTIAL_VAULT_INVALID));
    }

    @Test
    void ciphertextOrTagTamperFailsClosedAsInvalid() throws Exception {
        TestVault ciphertextFixture = newVault();
        ciphertextFixture.service().initialize(MASTER_PASSWORD);

        mutateEnvelope(ciphertextFixture.store().filePath(), "vault", "ciphertext",
                CredentialVaultServiceTest::flipFirstBase64Byte);
        assertVaultError(
                () -> ciphertextFixture.service().unlock(MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_INVALID);

        TestVault tagFixture = newVault();
        tagFixture.service().initialize(MASTER_PASSWORD);
        mutateEnvelope(tagFixture.store().filePath(), "vault", "ciphertext",
                CredentialVaultServiceTest::flipLastBase64Byte);
        assertVaultError(
                () -> tagFixture.service().unlock(MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_INVALID);
    }

    @Test
    void corruptEnvelopeUnknownVersionsInvalidDocumentAndDuplicateIdsFailClosedAndPreserveFile() throws Exception {
        TestVault fixture = newVault();
        fixture.service().initialize(MASTER_PASSWORD);

        String original = Files.readString(fixture.store().filePath(), StandardCharsets.UTF_8);
        Files.writeString(fixture.store().filePath(), "{broken", StandardCharsets.UTF_8);
        assertVaultError(
                () -> fixture.service().unlock(MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_INVALID);
        assertThat(Files.readString(fixture.store().filePath(), StandardCharsets.UTF_8)).isEqualTo("{broken");

        Files.writeString(fixture.store().filePath(), original.replace("\"schemaVersion\" : 1", "\"schemaVersion\" : 99"),
                StandardCharsets.UTF_8);
        assertVaultError(
                () -> fixture.service().unlock(MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_INVALID);

        Files.writeString(fixture.store().filePath(), original.replace("\"cryptoVersion\" : 1", "\"cryptoVersion\" : 99"),
                StandardCharsets.UTF_8);
        assertVaultError(
                () -> fixture.service().unlock(MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_INVALID);

        TestVault duplicateFixture = newVault();
        CredentialVaultCrypto crypto = duplicateFixture.crypto();
        Instant now = duplicateFixture.clock().instant();
        CredentialVaultDocument duplicateDocument = new CredentialVaultDocument(
                CredentialVaultCrypto.SCHEMA_VERSION,
                List.of(
                        entry("cred_duplicate", CredentialType.GOOGLE_ACCOUNT, "Google", "a@school.test", "one", now),
                        entry("cred_duplicate", CredentialType.WINDOWS_ACCOUNT, "Windows", "PC01", "two", now)));
        duplicateFixture.store().writeNew(crypto.createEnvelope(MASTER_PASSWORD.toCharArray(), duplicateDocument));
        assertVaultError(
                () -> duplicateFixture.service().unlock(MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_INVALID);

        TestVault invalidTypeFixture = newVault();
        CredentialVaultDocument invalidTypeDocument = new CredentialVaultDocument(
                CredentialVaultCrypto.SCHEMA_VERSION,
                List.of(entry("cred_invalid", null, "Label", "login", "password", now)));
        invalidTypeFixture.store().writeNew(crypto.createEnvelope(MASTER_PASSWORD.toCharArray(), invalidTypeDocument));
        assertVaultError(
                () -> invalidTypeFixture.service().unlock(MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_INVALID);
    }

    @Test
    void sessionsExpireLazilyRenewOnValidAccessAndLockExplicitlyInvalidatesToken() {
        MutableClock clock = new MutableClock(Instant.parse("2026-09-03T18:00:00Z"));
        TestVault fixture = newVault(clock, new RecordingAuditSink(), new RecordingFileSecurity());
        fixture.service().initialize(MASTER_PASSWORD);
        CredentialVaultSession session = fixture.service().unlock(MASTER_PASSWORD);

        clock.advance(Duration.ofMinutes(4).plusSeconds(59));
        assertThat(fixture.service().list(session.token())).isEmpty();

        clock.advance(Duration.ofMinutes(4).plusSeconds(59));
        assertThat(fixture.service().list(session.token())).isEmpty();

        clock.advance(Duration.ofMinutes(5).plusSeconds(1));
        assertVaultError(
                () -> fixture.service().list(session.token()),
                ErrorCode.CREDENTIAL_VAULT_LOCKED);

        CredentialVaultSession next = fixture.service().unlock(MASTER_PASSWORD);
        fixture.service().lock(next.token());
        assertVaultError(
                () -> fixture.service().list(next.token()),
                ErrorCode.CREDENTIAL_VAULT_LOCKED);
        fixture.service().lock(next.token());

        assertThat(CredentialVaultSessionManager.class.getDeclaredFields())
                .allSatisfy(field -> assertThat(field.getType())
                        .isNotIn(ScheduledExecutorService.class, Timer.class));
    }

    @Test
    void addListRevealUpdateAndRemoveEntriesWithoutReturningPasswordsInList() {
        TestVault fixture = newVault();
        fixture.service().initialize(MASTER_PASSWORD);
        CredentialVaultSession session = fixture.service().unlock(MASTER_PASSWORD);

        CredentialVaultEntryMetadata windows = fixture.service().add(session.token(), new CredentialVaultEntryDraft(
                CredentialType.WINDOWS_ACCOUNT,
                "Windows PC01",
                "PC01",
                "windows secret"));
        CredentialVaultEntryMetadata google = fixture.service().add(session.token(), new CredentialVaultEntryDraft(
                CredentialType.GOOGLE_ACCOUNT,
                "Google Alicia",
                "alicia@school.test",
                "google secret"));

        assertThat(fixture.service().list(session.token()))
                .containsExactly(windows, google);
        assertThat(fixture.service().list(session.token()).toString())
                .doesNotContain("windows secret")
                .doesNotContain("google secret");
        assertThat(fixture.service().reveal(session.token(), google.credentialId()))
                .isEqualTo("google secret");

        CredentialVaultEntryMetadata updated = fixture.service().update(
                session.token(),
                windows.credentialId(),
                new CredentialVaultEntryUpdate("Windows PC01 updated", "DOMAIN\\PC01", "new windows secret"));
        assertThat(updated.credentialType()).isEqualTo(CredentialType.WINDOWS_ACCOUNT);
        assertThat(updated.updatedAtUtc()).isAfterOrEqualTo(updated.createdAtUtc());
        assertThat(fixture.service().reveal(session.token(), windows.credentialId()))
                .isEqualTo("new windows secret");

        fixture.service().remove(session.token(), google.credentialId());
        assertVaultError(
                () -> fixture.service().reveal(session.token(), google.credentialId()),
                ErrorCode.CREDENTIAL_NOT_FOUND);
    }

    @Test
    void missingCredentialAndInvalidDraftsReturnOperationalErrors() {
        TestVault fixture = newVault();
        fixture.service().initialize(MASTER_PASSWORD);
        CredentialVaultSession session = fixture.service().unlock(MASTER_PASSWORD);

        assertVaultError(
                () -> fixture.service().reveal(session.token(), "cred_missing"),
                ErrorCode.CREDENTIAL_NOT_FOUND);
        assertVaultError(
                () -> fixture.service().add(session.token(), new CredentialVaultEntryDraft(
                        null,
                        "Label",
                        "login",
                        "secret")),
                ErrorCode.INVALID_REQUEST);
        assertVaultError(
                () -> fixture.service().add(session.token(), new CredentialVaultEntryDraft(
                        CredentialType.GOOGLE_ACCOUNT,
                        "Label",
                        "login",
                        "x".repeat(CredentialVaultLimits.CREDENTIAL_PASSWORD_MAX_CHARS + 1))),
                ErrorCode.INVALID_REQUEST);
    }

    @Test
    void unicodePasswordsWithSpacesArePreservedWithoutNormalization() {
        TestVault fixture = newVault();
        fixture.service().initialize(MASTER_PASSWORD);
        CredentialVaultSession session = fixture.service().unlock(MASTER_PASSWORD);
        String secret = "  contraseña con espacios y símbolos Ω !  ";

        CredentialVaultEntryMetadata entry = fixture.service().add(session.token(), new CredentialVaultEntryDraft(
                CredentialType.GOOGLE_ACCOUNT,
                "Google",
                "nina@school.test",
                secret));

        assertThat(fixture.service().reveal(session.token(), entry.credentialId())).isEqualTo(secret);
    }

    @Test
    void masterPasswordChangeRewrapsDekKeepsVaultCiphertextAndInvalidatesSession() {
        TestVault fixture = newVault();
        fixture.service().initialize(MASTER_PASSWORD);
        CredentialVaultSession session = fixture.service().unlock(MASTER_PASSWORD);
        CredentialVaultEntryMetadata entry = fixture.service().add(session.token(), new CredentialVaultEntryDraft(
                CredentialType.GOOGLE_ACCOUNT,
                "Google",
                "student@school.test",
                "student secret"));
        CredentialVaultEnvelope before = fixture.store().readEnvelope();

        fixture.service().changeMasterPassword(session.token(), NEW_MASTER_PASSWORD);
        CredentialVaultEnvelope after = fixture.store().readEnvelope();

        assertThat(after.vault()).isEqualTo(before.vault());
        assertThat(after.kdf().salt()).isNotEqualTo(before.kdf().salt());
        assertThat(after.wrappedKey()).isNotEqualTo(before.wrappedKey());
        assertVaultError(
                () -> fixture.service().list(session.token()),
                ErrorCode.CREDENTIAL_VAULT_LOCKED);
        assertVaultError(
                () -> fixture.service().unlock(MASTER_PASSWORD),
                ErrorCode.CREDENTIAL_VAULT_UNLOCK_FAILED);

        CredentialVaultSession newSession = fixture.service().unlock(NEW_MASTER_PASSWORD);
        assertThat(fixture.service().reveal(newSession.token(), entry.credentialId()))
                .isEqualTo("student secret");
    }

    @Test
    void vaultNonceChangesBetweenWritesAndSerializedEnvelopeKeepsBusinessDataEncrypted() throws Exception {
        TestVault fixture = newVault();
        fixture.service().initialize(MASTER_PASSWORD);
        CredentialVaultSession session = fixture.service().unlock(MASTER_PASSWORD);
        CredentialVaultEnvelope initialized = fixture.store().readEnvelope();
        CredentialVaultEntryMetadata entry = fixture.service().add(session.token(), new CredentialVaultEntryDraft(
                CredentialType.WINDOWS_ACCOUNT,
                "PC unicode",
                "DOMAIN\\unicode",
                "secret with unicode ñ"));
        CredentialVaultEnvelope afterAdd = fixture.store().readEnvelope();
        fixture.service().update(session.token(), entry.credentialId(), new CredentialVaultEntryUpdate(
                "PC unicode 2",
                "DOMAIN\\unicode2",
                "another secret"));
        CredentialVaultEnvelope afterUpdate = fixture.store().readEnvelope();

        assertThat(afterAdd.vault().nonce()).isNotEqualTo(initialized.vault().nonce());
        assertThat(afterUpdate.vault().nonce()).isNotEqualTo(afterAdd.vault().nonce());
        assertThat(Files.readString(fixture.store().filePath(), StandardCharsets.UTF_8))
                .doesNotContain("PC unicode")
                .doesNotContain("DOMAIN\\unicode")
                .doesNotContain("secret with unicode ñ")
                .doesNotContain("another secret");
    }

    @Test
    void modelToStringExceptionsAndAuditSinkDoNotExposeSecrets() {
        RecordingAuditSink audit = new RecordingAuditSink();
        TestVault fixture = newVault(new MutableClock(Instant.parse("2026-09-03T18:00:00Z")), audit,
                new RecordingFileSecurity());
        fixture.service().initialize(MASTER_PASSWORD);
        CredentialVaultSession session = fixture.service().unlock(MASTER_PASSWORD);
        CredentialVaultEntryMetadata entry = fixture.service().add(session.token(), new CredentialVaultEntryDraft(
                CredentialType.WINDOWS_ACCOUNT,
                "Visible label",
                "secret-login",
                "credential secret"));

        assertThat(new CredentialVaultEntry(
                entry.credentialId(),
                CredentialType.WINDOWS_ACCOUNT,
                "Visible label",
                "secret-login",
                "credential secret",
                entry.createdAtUtc(),
                entry.updatedAtUtc()).toString()).doesNotContain("credential secret");
        assertThatThrownBy(() -> fixture.service().unlock(MASTER_PASSWORD + "nope"))
                .hasMessageNotContaining(MASTER_PASSWORD)
                .hasMessageNotContaining("credential secret");
        assertThat(audit.events().toString())
                .doesNotContain(MASTER_PASSWORD)
                .doesNotContain("credential secret")
                .doesNotContain("secret-login");
    }

    private TestVault newVault() {
        return newVault(new MutableClock(Instant.parse("2026-09-03T18:00:00Z")), new RecordingAuditSink(),
                new RecordingFileSecurity());
    }

    private TestVault newVault(
            MutableClock clock,
            RecordingAuditSink auditSink,
            RecordingFileSecurity fileSecurity) {
        CredentialVaultStore store = new CredentialVaultStore(tempDir.resolve("vault-" + fixtureNumber++), fileSecurity);
        CredentialVaultCryptoParameters parameters = new CredentialVaultCryptoParameters(2, Duration.ofMinutes(5));
        CredentialVaultCrypto crypto = new CredentialVaultCrypto(parameters, new SecureRandom());
        CredentialVaultSessionManager sessionManager = new CredentialVaultSessionManager(
                clock,
                parameters.sessionIdleTimeout(),
                new SecureRandom());
        CredentialVaultService service = new CredentialVaultService(
                store,
                crypto,
                sessionManager,
                auditSink,
                clock,
                new SecureRandom());
        return new TestVault(store, crypto, service, clock, auditSink, fileSecurity);
    }

    private void mutateEnvelope(
            Path path,
            String objectName,
            String fieldName,
            java.util.function.Function<String, String> mutation) throws Exception {
        JsonNode root = OBJECT_MAPPER.readTree(path.toFile());
        ObjectNode nested = (ObjectNode) root.get(objectName);
        nested.put(fieldName, mutation.apply(nested.get(fieldName).asText()));
        Files.writeString(path, OBJECT_MAPPER.writerWithDefaultPrettyPrinter().writeValueAsString(root),
                StandardCharsets.UTF_8);
    }

    private static String flipFirstBase64Byte(String base64) {
        byte[] bytes = Base64.getDecoder().decode(base64);
        bytes[0] = (byte) (bytes[0] ^ 0x01);
        return Base64.getEncoder().encodeToString(bytes);
    }

    private static String flipLastBase64Byte(String base64) {
        byte[] bytes = Base64.getDecoder().decode(base64);
        bytes[bytes.length - 1] = (byte) (bytes[bytes.length - 1] ^ 0x01);
        return Base64.getEncoder().encodeToString(bytes);
    }

    private static CredentialVaultEntry entry(
            String credentialId,
            CredentialType credentialType,
            String displayName,
            String loginIdentifier,
            String password,
            Instant now) {
        return new CredentialVaultEntry(credentialId, credentialType, displayName, loginIdentifier, password, now, now);
    }

    private static void assertVaultError(Runnable runnable, ErrorCode errorCode, String... forbidden) {
        assertThatThrownBy(runnable::run)
                .isInstanceOf(CredentialVaultException.class)
                .satisfies(throwable -> {
                    CredentialVaultException exception = (CredentialVaultException) throwable;
                    assertThat(exception.errorCode()).isEqualTo(errorCode);
                    for (String secret : forbidden) {
                        assertThat(exception.getMessage()).doesNotContain(secret);
                    }
                });
    }

    private record TestVault(
            CredentialVaultStore store,
            CredentialVaultCrypto crypto,
            CredentialVaultService service,
            MutableClock clock,
            RecordingAuditSink auditSink,
            RecordingFileSecurity fileSecurity) {
    }

    private static final class RecordingAuditSink implements CredentialVaultAuditSink {

        private final List<String> events = new ArrayList<>();

        @Override
        public void vaultInitialized() {
            events.add("initialized");
        }

        @Override
        public void vaultUnlocked() {
            events.add("unlocked");
        }

        @Override
        public void vaultLocked() {
            events.add("locked");
        }

        @Override
        public void credentialAdded(String credentialId) {
            events.add("added:" + credentialId);
        }

        @Override
        public void credentialUpdated(String credentialId) {
            events.add("updated:" + credentialId);
        }

        @Override
        public void credentialRemoved(String credentialId) {
            events.add("removed:" + credentialId);
        }

        @Override
        public void credentialRevealed(String credentialId) {
            events.add("revealed:" + credentialId);
        }

        List<String> events() {
            return events;
        }
    }

    private static final class RecordingFileSecurity implements CredentialVaultFileSecurity {

        private final List<Path> protectedPaths = new ArrayList<>();

        @Override
        public void protect(Path path) {
            protectedPaths.add(path);
        }

        List<Path> protectedPaths() {
            return protectedPaths;
        }
    }

    private static final class MutableClock extends Clock {

        private Instant instant;

        private MutableClock(Instant instant) {
            this.instant = instant;
        }

        @Override
        public ZoneId getZone() {
            return ZoneId.of("UTC");
        }

        @Override
        public Clock withZone(ZoneId zone) {
            return this;
        }

        @Override
        public Instant instant() {
            return instant;
        }

        void advance(Duration duration) {
            instant = instant.plus(duration);
        }
    }
}
