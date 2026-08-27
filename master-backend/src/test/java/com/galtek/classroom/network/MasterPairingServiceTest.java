package com.galtek.classroom.network;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.operations.ErrorCode;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneId;
import java.util.Base64;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class MasterPairingServiceTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-08-27T15:00:00Z");

    @TempDir
    private Path tempDir;

    @Test
    void masterNetworkIdentityCreatesProtectedKeyMaterialAndReopensPublicMetadata() throws Exception {
        Path dataDir = tempDir.resolve("master-identity");
        var keyStore = new FileMasterNetworkIdentityKeyStore(dataDir);
        var resolver = new MasterNetworkIdentityResolver(
                new MasterNetworkIdentityStore(dataDir),
                keyStore,
                Clock.fixed(FIXED_NOW, ZoneId.of("UTC")));

        MasterNetworkIdentityResolution first = resolver.resolve();
        MasterNetworkIdentityResolution second = new MasterNetworkIdentityResolver(
                new MasterNetworkIdentityStore(dataDir),
                keyStore,
                Clock.fixed(FIXED_NOW.plusSeconds(60), ZoneId.of("UTC")))
                .resolve();

        assertThat(first.status()).isEqualTo(MasterNetworkIdentityStatus.READY);
        assertThat(first.created()).isTrue();
        assertThat(second.status()).isEqualTo(MasterNetworkIdentityStatus.READY);
        assertThat(second.created()).isFalse();
        assertThat(second.metadata().masterNetworkIdentityId())
                .isEqualTo(first.metadata().masterNetworkIdentityId());
        assertThat(second.metadata().publicKeyFingerprint())
                .isEqualTo(first.metadata().publicKeyFingerprint());

        String metadataJson = Files.readString(dataDir.resolve(PairingConstants.MASTER_NETWORK_IDENTITY_FILE_NAME));
        assertThat(metadataJson)
                .contains("publicKeyFingerprint")
                .doesNotContain("privateKey")
                .doesNotContain("BEGIN PRIVATE KEY");
        assertThat(Files.exists(dataDir.resolve(PairingConstants.MASTER_NETWORK_PRIVATE_KEY_FILE_NAME))).isTrue();
        assertThat(Files.exists(dataDir.resolve(PairingConstants.MASTER_NETWORK_PROTECTOR_FILE_NAME))).isTrue();
    }

    @Test
    void validPairingPersistsClientTrustAndAuthorizesClient() {
        Fixture fixture = createFixture("valid");
        TestClientIdentity client = TestClientIdentity.create();

        MasterPairingChallengeResult challenge = fixture.service.createPairingChallenge(client.descriptor(), true);
        MasterPairingCompletionResult completed = fixture.service.completePairing(client.responseTo(challenge.challenge()));
        MasterClientAuthorization authorization = fixture.service.isClientAuthorized(client.descriptor());

        assertThat(challenge.created()).isTrue();
        assertThat(completed.paired()).isTrue();
        assertThat(authorization.authorized()).isTrue();

        MasterTrustDocument document = fixture.trustStore.read().document();
        assertThat(document.pairedClients()).hasSize(1);
        assertThat(document.pairingChallenges()).hasSize(1);
        assertThat(document.pairedClients().getFirst().status()).isEqualTo(PairingStatus.PAIRED);
        assertThat(document.pairedClients().getFirst().clientNetworkIdentityId())
                .isEqualTo(client.descriptor().clientNetworkIdentityId());
        assertThat(document.pairedClients().getFirst().clientInstallationId())
                .isEqualTo(client.descriptor().clientInstallationId());
        assertThat(document.pairedClients().getFirst().pairedAtUtc()).isNotNull();
        assertThat(document.pairedClients().getFirst().revokedAtUtc()).isNull();
    }

    @Test
    void invalidClientSignatureIsRejected() {
        Fixture fixture = createFixture("invalid-signature");
        TestClientIdentity client = TestClientIdentity.create();
        TestClientIdentity attacker = TestClientIdentity.create();
        PairingChallenge challenge = fixture.service.createPairingChallenge(client.descriptor(), true).challenge();

        MasterPairingCompletionResult completed = fixture.service.completePairing(attacker.responseTo(challenge));

        assertThat(completed.paired()).isFalse();
        assertThat(completed.errorCode()).isEqualTo(PairingConstants.SIGNATURE_INVALID);
        assertThat(fixture.service.isClientAuthorized(client.descriptor()).authorized()).isFalse();
    }

    @Test
    void expiredChallengeIsRejected() {
        MutableClock clock = new MutableClock(FIXED_NOW);
        Fixture fixture = createFixture("expired", clock);
        TestClientIdentity client = TestClientIdentity.create();
        PairingChallenge challenge = fixture.service.createPairingChallenge(client.descriptor(), true).challenge();
        clock.instant = FIXED_NOW.plus(PairingConstants.CHALLENGE_TTL).plusSeconds(1);

        MasterPairingCompletionResult completed = fixture.service.completePairing(client.responseTo(challenge));

        assertThat(completed.paired()).isFalse();
        assertThat(completed.errorCode()).isEqualTo(PairingConstants.CHALLENGE_EXPIRED);
        assertThat(fixture.service.isClientAuthorized(client.descriptor()).authorized()).isFalse();
    }

    @Test
    void consumedChallengeCannotBeReplayed() {
        Fixture fixture = createFixture("replay");
        TestClientIdentity client = TestClientIdentity.create();
        PairingChallenge challenge = fixture.service.createPairingChallenge(client.descriptor(), true).challenge();
        PairingResponse response = client.responseTo(challenge);

        MasterPairingCompletionResult first = fixture.service.completePairing(response);
        MasterPairingCompletionResult replay = fixture.service.completePairing(response);

        assertThat(first.paired()).isTrue();
        assertThat(replay.paired()).isFalse();
        assertThat(replay.errorCode()).isEqualTo(PairingConstants.REPLAY_REJECTED);
        assertThat(fixture.trustStore.read().document().pairingChallenges().getFirst().status())
                .isEqualTo(PairingStatus.PAIRED);
    }

    @Test
    void incorrectClientFingerprintIsRejectedBeforeChallengeIsIssued() {
        Fixture fixture = createFixture("fingerprint");
        TestClientIdentity client = TestClientIdentity.create();
        ClientNetworkIdentityDescriptor tampered = new ClientNetworkIdentityDescriptor(
                client.descriptor().clientNetworkIdentityId(),
                client.descriptor().clientInstallationId(),
                "0".repeat(64),
                client.descriptor().subjectPublicKeyInfoBase64());

        MasterPairingChallengeResult challenge = fixture.service.createPairingChallenge(tampered, true);

        assertThat(challenge.created()).isFalse();
        assertThat(challenge.errorCode()).isEqualTo(PairingConstants.FINGERPRINT_MISMATCH);
        assertThat(fixture.trustStore.read().status()).isEqualTo(MasterTrustStoreReadStatus.MISSING);
    }

    @Test
    void pairedClientRemainsAuthorizedAfterTrustStoreReopens() {
        Fixture fixture = createFixture("reopen");
        TestClientIdentity client = TestClientIdentity.create();
        PairingChallenge challenge = fixture.service.createPairingChallenge(client.descriptor(), true).challenge();
        fixture.service.completePairing(client.responseTo(challenge));

        Fixture reopened = createFixture("reopen", fixture.clock, fixture.keyStore);
        MasterClientAuthorization authorization = reopened.service.isClientAuthorized(client.descriptor());

        assertThat(authorization.authorized()).isTrue();
    }

    @Test
    void revocationBlocksFutureAuthorizationWithoutDeletingTrustRecord() {
        Fixture fixture = createFixture("revocation");
        TestClientIdentity client = TestClientIdentity.create();
        PairingChallenge challenge = fixture.service.createPairingChallenge(client.descriptor(), true).challenge();
        fixture.service.completePairing(client.responseTo(challenge));

        boolean revoked = fixture.service.revokeClient(client.descriptor().clientNetworkIdentityId());
        MasterClientAuthorization authorization = fixture.service.isClientAuthorized(client.descriptor());

        assertThat(revoked).isTrue();
        assertThat(authorization.authorized()).isFalse();
        assertThat(authorization.status()).isEqualTo(PairingStatus.REVOKED);
        assertThat(authorization.errorCode()).isEqualTo(ErrorCode.MASTER_NOT_PAIRED);
        assertThat(fixture.trustStore.read().document().pairedClients().getFirst().status())
                .isEqualTo(PairingStatus.REVOKED);
    }

    @Test
    void unpairedClientFailsClosed() {
        Fixture fixture = createFixture("unpaired");
        TestClientIdentity client = TestClientIdentity.create();

        MasterClientAuthorization authorization = fixture.service.isClientAuthorized(client.descriptor());

        assertThat(authorization.authorized()).isFalse();
        assertThat(authorization.status()).isEqualTo(PairingStatus.UNPAIRED);
        assertThat(authorization.errorCode()).isEqualTo(ErrorCode.MASTER_NOT_PAIRED);
    }

    @Test
    void masterTrustSupportsMultipleClients() {
        Fixture fixture = createFixture("multiple-clients");
        TestClientIdentity clientA = TestClientIdentity.create();
        TestClientIdentity clientB = TestClientIdentity.create();

        PairingChallenge challengeA = fixture.service.createPairingChallenge(clientA.descriptor(), true).challenge();
        PairingChallenge challengeB = fixture.service.createPairingChallenge(clientB.descriptor(), true).challenge();
        fixture.service.completePairing(clientA.responseTo(challengeA));
        fixture.service.completePairing(clientB.responseTo(challengeB));

        assertThat(fixture.service.isClientAuthorized(clientA.descriptor()).authorized()).isTrue();
        assertThat(fixture.service.isClientAuthorized(clientB.descriptor()).authorized()).isTrue();
        assertThat(fixture.trustStore.read().document().pairedClients()).hasSize(2);
    }

    private Fixture createFixture(String name) {
        return createFixture(name, new MutableClock(FIXED_NOW));
    }

    private Fixture createFixture(String name, MutableClock clock) {
        return createFixture(name, clock, new InMemoryMasterNetworkIdentityKeyStore());
    }

    private Fixture createFixture(
            String name,
            MutableClock clock,
            InMemoryMasterNetworkIdentityKeyStore keyStore) {
        Path dataDir = tempDir.resolve(name);
        var identityStore = new MasterNetworkIdentityStore(dataDir);
        var identityResolver = new MasterNetworkIdentityResolver(identityStore, keyStore, clock);
        var trustStore = new MasterTrustStore(dataDir);
        var service = new MasterPairingService(identityResolver, keyStore, trustStore, clock, new SecureRandom());
        return new Fixture(clock, keyStore, trustStore, service);
    }

    private record Fixture(
            MutableClock clock,
            InMemoryMasterNetworkIdentityKeyStore keyStore,
            MasterTrustStore trustStore,
            MasterPairingService service) {
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
    }

    private static final class InMemoryMasterNetworkIdentityKeyStore implements MasterNetworkIdentityKeyStore {
        private final Map<String, KeyPair> keys = new HashMap<>();

        @Override
        public boolean hasAnyKeyMaterial() {
            return !keys.isEmpty();
        }

        @Override
        public MasterNetworkKeyCreationResult create(String keyId) {
            if (keys.containsKey(keyId)) {
                return MasterNetworkKeyCreationResult.alreadyExists();
            }

            KeyPair keyPair = generateKeyPair();
            keys.put(keyId, keyPair);
            byte[] publicKey = keyPair.getPublic().getEncoded();
            return MasterNetworkKeyCreationResult.created(
                    NetworkIdentityCrypto.fingerprint(publicKey),
                    Base64.getEncoder().encodeToString(publicKey));
        }

        @Override
        public MasterNetworkKeyLookupResult lookup(String keyId) {
            KeyPair keyPair = keys.get(keyId);
            if (keyPair == null) {
                return MasterNetworkKeyLookupResult.missing();
            }

            byte[] publicKey = keyPair.getPublic().getEncoded();
            return MasterNetworkKeyLookupResult.found(
                    NetworkIdentityCrypto.fingerprint(publicKey),
                    Base64.getEncoder().encodeToString(publicKey));
        }

        @Override
        public MasterNetworkSignatureResult sign(String keyId, byte[] data) {
            KeyPair keyPair = keys.get(keyId);
            if (keyPair == null) {
                return MasterNetworkSignatureResult.missing();
            }

            return MasterNetworkSignatureResult.signed(MasterPairingServiceTest.sign(keyPair.getPrivate(), data));
        }
    }

    private record TestClientIdentity(
            ClientNetworkIdentityDescriptor descriptor,
            PrivateKey privateKey) {

        static TestClientIdentity create() {
            KeyPair keyPair = generateKeyPair();
            byte[] publicKey = keyPair.getPublic().getEncoded();
            String publicKeyBase64 = Base64.getEncoder().encodeToString(publicKey);
            return new TestClientIdentity(
                    new ClientNetworkIdentityDescriptor(
                            UUID.randomUUID(),
                            UUID.randomUUID(),
                            NetworkIdentityCrypto.fingerprint(publicKey),
                            publicKeyBase64),
                    keyPair.getPrivate());
        }

        PairingResponse responseTo(PairingChallenge challenge) {
            PairingResponse unsigned = new PairingResponse(
                    PairingConstants.SCHEMA_VERSION,
                    PairingConstants.PURPOSE,
                    challenge.challengeId(),
                    challenge.masterNetworkIdentityId(),
                    challenge.clientNetworkIdentityId(),
                    challenge.clientInstallationId(),
                    challenge.masterPublicKeyFingerprint(),
                    challenge.clientPublicKeyFingerprint(),
                    challenge.nonceBase64(),
                    NetworkIdentityCrypto.createNonceBase64(new SecureRandom()),
                    challenge.issuedAtUtc().plusSeconds(10),
                    "");
            return new PairingResponse(
                    unsigned.schemaVersion(),
                    unsigned.purpose(),
                    unsigned.challengeId(),
                    unsigned.masterNetworkIdentityId(),
                    unsigned.clientNetworkIdentityId(),
                    unsigned.clientInstallationId(),
                    unsigned.masterPublicKeyFingerprint(),
                    unsigned.clientPublicKeyFingerprint(),
                    unsigned.challengeNonceBase64(),
                    unsigned.responseNonceBase64(),
                    unsigned.signedAtUtc(),
                    sign(privateKey, NetworkIdentityCrypto.canonicalResponseBytes(unsigned)));
        }
    }

    private static KeyPair generateKeyPair() {
        try {
            KeyPairGenerator generator = KeyPairGenerator.getInstance("RSA");
            generator.initialize(PairingConstants.RSA_KEY_SIZE_BITS);
            return generator.generateKeyPair();
        } catch (Exception exception) {
            throw new IllegalStateException("RSA is not available.", exception);
        }
    }

    private static String sign(PrivateKey privateKey, byte[] data) {
        try {
            Signature signature = Signature.getInstance("SHA256withRSA");
            signature.initSign(privateKey);
            signature.update(data);
            return Base64.getEncoder().encodeToString(signature.sign());
        } catch (Exception exception) {
            throw new IllegalStateException("SHA256withRSA is not available.", exception);
        }
    }
}
