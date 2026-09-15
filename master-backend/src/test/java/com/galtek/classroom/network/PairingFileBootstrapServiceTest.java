package com.galtek.classroom.network;

import static org.assertj.core.api.Assertions.assertThat;

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
import org.bouncycastle.asn1.x509.KeyPurposeId;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class PairingFileBootstrapServiceTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-09-14T12:00:00Z");

    @TempDir
    private Path tempDir;

    @Test
    void createChallenge_WithDescriptorAndExplicitIntent_WritesChallengeWithoutPrivateKey() throws Exception {
        Fixture fixture = createFixture("create-challenge");
        TestClientIdentity client = TestClientIdentity.create();
        Path descriptorPath = tempDir.resolve("client-descriptor.json");
        Path challengePath = tempDir.resolve("challenge.json");
        writeDescriptor(descriptorPath, client.descriptor());

        PairingFileBootstrapResult result = fixture.bootstrap.createChallenge(
                descriptorPath.toString(),
                challengePath.toString(),
                true);
        String challengeJson = Files.readString(challengePath);

        assertThat(result.succeeded()).isTrue();
        assertThat(result.status()).isEqualTo(PairingStatus.PAIRING_PENDING);
        assertThat(result.challengeId()).isNotNull();
        assertThat(challengeJson)
                .contains("masterSignatureBase64")
                .doesNotContain("private")
                .doesNotContain("BEGIN PRIVATE KEY");
        assertThat(fixture.trustStore.read().document().pairedClients().getFirst().status())
                .isEqualTo(PairingStatus.PAIRING_PENDING);
    }

    @Test
    void createChallenge_WithoutExplicitIntent_RejectsAndDoesNotWriteChallenge() throws Exception {
        Fixture fixture = createFixture("no-intent");
        TestClientIdentity client = TestClientIdentity.create();
        Path descriptorPath = tempDir.resolve("client-descriptor.json");
        Path challengePath = tempDir.resolve("challenge.json");
        writeDescriptor(descriptorPath, client.descriptor());

        PairingFileBootstrapResult result = fixture.bootstrap.createChallenge(
                descriptorPath.toString(),
                challengePath.toString(),
                false);

        assertThat(result.succeeded()).isFalse();
        assertThat(result.errorCode()).isEqualTo(PairingConstants.EXPLICIT_INTENT_REQUIRED);
        assertThat(Files.exists(challengePath)).isFalse();
    }

    @Test
    void completePairing_WithValidResponse_PersistsPairedTrust() throws Exception {
        Fixture fixture = createFixture("complete");
        TestClientIdentity client = TestClientIdentity.create();
        Path descriptorPath = tempDir.resolve("client-descriptor.json");
        Path challengePath = tempDir.resolve("challenge.json");
        Path responsePath = tempDir.resolve("response.json");
        writeDescriptor(descriptorPath, client.descriptor());
        fixture.bootstrap.createChallenge(descriptorPath.toString(), challengePath.toString(), true);
        PairingChallenge challenge = fixture.objectMapper.readValue(challengePath.toFile(), PairingChallenge.class);
        fixture.objectMapper.writerWithDefaultPrettyPrinter()
                .writeValue(responsePath.toFile(), client.responseTo(challenge));

        PairingFileBootstrapResult result = fixture.bootstrap.completePairing(responsePath.toString());

        assertThat(result.succeeded()).isTrue();
        assertThat(result.status()).isEqualTo(PairingStatus.PAIRED);
        assertThat(fixture.trustStore.read().document().pairedClients().getFirst().status())
                .isEqualTo(PairingStatus.PAIRED);
    }

    @Test
    void completePairing_WithInvalidResponse_DoesNotFinalizePairing() throws Exception {
        Fixture fixture = createFixture("invalid-response");
        TestClientIdentity client = TestClientIdentity.create();
        Path descriptorPath = tempDir.resolve("client-descriptor.json");
        Path challengePath = tempDir.resolve("challenge.json");
        Path responsePath = tempDir.resolve("response.json");
        writeDescriptor(descriptorPath, client.descriptor());
        fixture.bootstrap.createChallenge(descriptorPath.toString(), challengePath.toString(), true);
        PairingChallenge challenge = fixture.objectMapper.readValue(challengePath.toFile(), PairingChallenge.class);
        fixture.objectMapper.writerWithDefaultPrettyPrinter()
                .writeValue(responsePath.toFile(), withClientSignature(client.responseTo(challenge), "not-base64"));

        PairingFileBootstrapResult result = fixture.bootstrap.completePairing(responsePath.toString());

        assertThat(result.succeeded()).isFalse();
        assertThat(result.errorCode()).isEqualTo(PairingConstants.SIGNATURE_INVALID);
        assertThat(fixture.trustStore.read().document().pairedClients().getFirst().status())
                .isEqualTo(PairingStatus.PAIRING_PENDING);
    }

    @Test
    void parse_WhenCreateChallengeIsMissingApproval_StillParsesForServiceRejection() {
        PairingAdminCommandLine command = PairingAdminCommandLine.parse(new String[] {
                "--pairing-create-challenge",
                "client.json",
                "--pairing-challenge-out",
                "challenge.json"
        });

        assertThat(command.valid()).isTrue();
        assertThat(command.mode()).isEqualTo(PairingAdminCommandMode.CREATE_CHALLENGE);
        assertThat(command.approvePairingIntent()).isFalse();
    }

    private Fixture createFixture(String name) {
        Path dataDir = tempDir.resolve(name);
        InMemoryMasterNetworkIdentityKeyStore keyStore = new InMemoryMasterNetworkIdentityKeyStore();
        MasterTrustStore trustStore = new MasterTrustStore(dataDir);
        MasterPairingService pairingService = new MasterPairingService(
                new MasterNetworkIdentityResolver(
                        new MasterNetworkIdentityStore(dataDir),
                        keyStore,
                        Clock.fixed(FIXED_NOW, ZoneId.of("UTC"))),
                keyStore,
                trustStore,
                Clock.fixed(FIXED_NOW, ZoneId.of("UTC")),
                new SecureRandom());
        return new Fixture(
                trustStore,
                new com.fasterxml.jackson.databind.ObjectMapper().findAndRegisterModules(),
                new PairingFileBootstrapService(pairingService));
    }

    private static void writeDescriptor(Path path, ClientNetworkIdentityDescriptor descriptor) throws Exception {
        String json = """
                {
                  "schemaVersion": 1,
                  "purpose": "GALTEK_CLASSROOM_CLIENT_PAIRING_DESCRIPTOR_V1",
                  "installationId": "%s",
                  "networkIdentityId": "%s",
                  "publicKeyFingerprint": "%s",
                  "publicKeySubjectPublicKeyInfoBase64": "%s",
                  "exportedAtUtc": "2026-09-14T12:00:00Z"
                }
                """.formatted(
                descriptor.clientInstallationId(),
                descriptor.clientNetworkIdentityId(),
                descriptor.publicKeyFingerprint(),
                descriptor.subjectPublicKeyInfoBase64());
        Files.writeString(path, json);
    }

    private record Fixture(
            MasterTrustStore trustStore,
            com.fasterxml.jackson.databind.ObjectMapper objectMapper,
            PairingFileBootstrapService bootstrap) {
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

            return MasterNetworkSignatureResult.signed(signWith(keyPair.getPrivate(), data));
        }

        @Override
        public MasterNetworkTlsIdentityResult tlsIdentity(String keyId) {
            KeyPair keyPair = keys.get(keyId);
            if (keyPair == null) {
                return MasterNetworkTlsIdentityResult.missing();
            }

            byte[] publicKey = keyPair.getPublic().getEncoded();
            return MasterNetworkTlsIdentityResult.ready(
                    NetworkIdentityCrypto.fingerprint(publicKey),
                    NetworkIdentityCertificateFactory.createSelfSigned(
                            keyPair.getPublic(),
                            keyPair.getPrivate(),
                            "test-master",
                            Clock.fixed(FIXED_NOW, ZoneId.of("UTC")),
                            new SecureRandom(),
                            KeyPurposeId.id_kp_serverAuth),
                    keyPair.getPrivate());
        }
    }

    private record TestClientIdentity(
            ClientNetworkIdentityDescriptor descriptor,
            PrivateKey privateKey) {

        static TestClientIdentity create() {
            KeyPair keyPair = generateKeyPair();
            byte[] publicKey = keyPair.getPublic().getEncoded();
            return new TestClientIdentity(
                    new ClientNetworkIdentityDescriptor(
                            UUID.randomUUID(),
                            UUID.randomUUID(),
                            NetworkIdentityCrypto.fingerprint(publicKey),
                            Base64.getEncoder().encodeToString(publicKey)),
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
            return withClientSignature(unsigned, signWith(privateKey, NetworkIdentityCrypto.canonicalResponseBytes(unsigned)));
        }
    }

    private static PairingResponse withClientSignature(PairingResponse response, String signatureBase64) {
        return new PairingResponse(
                response.schemaVersion(),
                response.purpose(),
                response.challengeId(),
                response.masterNetworkIdentityId(),
                response.clientNetworkIdentityId(),
                response.clientInstallationId(),
                response.masterPublicKeyFingerprint(),
                response.clientPublicKeyFingerprint(),
                response.challengeNonceBase64(),
                response.responseNonceBase64(),
                response.signedAtUtc(),
                signatureBase64);
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

    private static String signWith(PrivateKey privateKey, byte[] data) {
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
