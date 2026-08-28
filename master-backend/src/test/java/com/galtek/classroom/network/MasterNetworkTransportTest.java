package com.galtek.classroom.network;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.network.v1.ClientEnvelope;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.ConnectionState;
import com.galtek.classroom.network.v1.Heartbeat;
import com.galtek.classroom.network.v1.MasterEnvelope;
import com.galtek.classroom.network.v1.NetworkCapability;
import io.grpc.Context;
import io.grpc.stub.StreamObserver;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.security.cert.X509Certificate;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.util.ArrayList;
import java.util.Base64;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import org.bouncycastle.asn1.x509.KeyPurposeId;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class MasterNetworkTransportTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-08-27T16:00:00Z");

    @TempDir
    private java.nio.file.Path tempDir;

    @Test
    void pairedClientConnectsAndHeartbeatMaintainsOnline() throws Exception {
        Fixture fixture = createFixture("paired");
        TestClientIdentity client = TestClientIdentity.create("PC01");
        fixture.pair(client);

        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(fixture.service, client.fingerprint(), responses);

        requests.onNext(helloEnvelope(client, "PC01"));
        requests.onNext(heartbeatEnvelope("hb-1"));

        assertThat(responses.values()).hasSize(3);
        assertThat(responses.values().get(0).getConnectionStatus().getStatus())
                .isEqualTo(ConnectionState.CONNECTION_STATE_CONNECTING);
        assertThat(responses.values().get(1).getConnectionStatus().getStatus())
                .isEqualTo(ConnectionState.CONNECTION_STATE_ONLINE);
        assertThat(responses.values().get(2).getHeartbeatAck().getStatus())
                .isEqualTo(ConnectionState.CONNECTION_STATE_ONLINE);
        assertThat(fixture.registry.find(client.descriptor().clientNetworkIdentityId()))
                .get()
                .extracting(ClientConnectionSnapshot::status)
                .isEqualTo(DeviceStatus.ONLINE);
        assertThat(fixture.registry.find(client.descriptor().clientNetworkIdentityId()))
                .get()
                .satisfies(snapshot -> {
                    assertThat(snapshot.registered()).isFalse();
                    assertThat(snapshot.deviceId()).isNull();
                    assertThat(snapshot.capabilities())
                            .containsExactlyInAnyOrder(
                                    DeviceCapability.HEARTBEAT_V1,
                                    DeviceCapability.OPERATION_FRAMEWORK_V1);
                    assertThat(snapshot.agentVersion()).isEqualTo("0.5.0-test");
                });
    }

    @Test
    void unpairedClientIsRejected() throws Exception {
        Fixture fixture = createFixture("unpaired");
        TestClientIdentity client = TestClientIdentity.create("PC01");
        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(fixture.service, client.fingerprint(), responses);

        requests.onNext(helloEnvelope(client, "PC01"));

        assertRejected(responses, MasterNetworkTransportConstants.CLIENT_NOT_PAIRED);
        assertThat(fixture.registry.snapshots()).isEmpty();
    }

    @Test
    void revokedClientIsRejectedAndNeverOnline() throws Exception {
        Fixture fixture = createFixture("revoked");
        TestClientIdentity client = TestClientIdentity.create("PC01");
        fixture.pair(client);
        fixture.pairingService.revokeClient(client.descriptor().clientNetworkIdentityId());
        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(fixture.service, client.fingerprint(), responses);

        requests.onNext(helloEnvelope(client, "PC01"));

        assertRejected(responses, MasterNetworkTransportConstants.CLIENT_REVOKED);
        assertThat(fixture.registry.find(client.descriptor().clientNetworkIdentityId())).isEmpty();
    }

    @Test
    void revocationDuringOpenStreamRejectsNextHeartbeatAndMarksOffline() throws Exception {
        Fixture fixture = createFixture("revoked-open-stream");
        TestClientIdentity client = TestClientIdentity.create("PC01");
        fixture.pair(client);
        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(fixture.service, client.fingerprint(), responses);
        requests.onNext(helloEnvelope(client, "PC01"));

        fixture.pairingService.revokeClient(client.descriptor().clientNetworkIdentityId());
        requests.onNext(heartbeatEnvelope("hb-revoked"));

        assertThat(responses.values().getLast().getConnectionStatus().getStatus())
                .isEqualTo(ConnectionState.CONNECTION_STATE_REJECTED);
        assertThat(responses.values().getLast().getConnectionStatus().getReasonCode())
                .isEqualTo(MasterNetworkTransportConstants.CLIENT_REVOKED);
        assertThat(fixture.registry.find(client.descriptor().clientNetworkIdentityId()))
                .get()
                .extracting(ClientConnectionSnapshot::status)
                .isEqualTo(DeviceStatus.OFFLINE);
    }

    @Test
    void certificateFingerprintMismatchIsRejected() throws Exception {
        Fixture fixture = createFixture("fingerprint-mismatch");
        TestClientIdentity client = TestClientIdentity.create("PC01");
        TestClientIdentity attacker = TestClientIdentity.create("attacker");
        fixture.pair(client);
        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(fixture.service, attacker.fingerprint(), responses);

        requests.onNext(helloEnvelope(client, "PC01"));

        assertRejected(responses, MasterNetworkTransportConstants.CERTIFICATE_FINGERPRINT_MISMATCH);
        assertThat(fixture.registry.find(client.descriptor().clientNetworkIdentityId())).isEmpty();
    }

    @Test
    void unknownPeerIsRejected() throws Exception {
        Fixture fixture = createFixture("unknown-peer");
        TestClientIdentity paired = TestClientIdentity.create("PC01");
        TestClientIdentity unknown = TestClientIdentity.create("PC02");
        fixture.pair(paired);
        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(fixture.service, unknown.fingerprint(), responses);

        requests.onNext(helloEnvelope(unknown, "PC02"));

        assertRejected(responses, MasterNetworkTransportConstants.CLIENT_NOT_PAIRED);
        assertThat(fixture.registry.find(unknown.descriptor().clientNetworkIdentityId())).isEmpty();
    }

    @Test
    void heartbeatTimeoutChangesDeviceOffline() throws Exception {
        MutableClock clock = new MutableClock(FIXED_NOW);
        Fixture fixture = createFixture("timeout", clock);
        TestClientIdentity client = TestClientIdentity.create("PC01");
        fixture.pair(client);
        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(fixture.service, client.fingerprint(), responses);
        requests.onNext(helloEnvelope(client, "PC01"));

        clock.instant = FIXED_NOW.plusSeconds(46);
        int expired = fixture.registry.expireTimedOut(Duration.ofSeconds(45));

        assertThat(expired).isEqualTo(1);
        assertThat(fixture.registry.find(client.descriptor().clientNetworkIdentityId()))
                .get()
                .extracting(ClientConnectionSnapshot::status)
                .isEqualTo(DeviceStatus.OFFLINE);
    }

    @Test
    void reconnectAfterDisconnectReturnsClientOnline() throws Exception {
        Fixture fixture = createFixture("reconnect");
        TestClientIdentity client = TestClientIdentity.create("PC01");
        fixture.pair(client);
        RecordingObserver<MasterEnvelope> firstResponses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> first = openStream(fixture.service, client.fingerprint(), firstResponses);
        first.onNext(helloEnvelope(client, "PC01"));
        first.onCompleted();

        assertThat(fixture.registry.find(client.descriptor().clientNetworkIdentityId()))
                .get()
                .extracting(ClientConnectionSnapshot::status)
                .isEqualTo(DeviceStatus.OFFLINE);

        RecordingObserver<MasterEnvelope> secondResponses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> second = openStream(fixture.service, client.fingerprint(), secondResponses);
        second.onNext(helloEnvelope(client, "PC01"));

        assertThat(secondResponses.values().getLast().getConnectionStatus().getStatus())
                .isEqualTo(ConnectionState.CONNECTION_STATE_ONLINE);
        assertThat(fixture.registry.find(client.descriptor().clientNetworkIdentityId()))
                .get()
                .extracting(ClientConnectionSnapshot::status)
                .isEqualTo(DeviceStatus.ONLINE);
    }

    @Test
    void multipleClientsCanStayOnlineConcurrently() throws Exception {
        Fixture fixture = createFixture("multiple");
        TestClientIdentity clientA = TestClientIdentity.create("PC01");
        TestClientIdentity clientB = TestClientIdentity.create("PC02");
        fixture.pair(clientA);
        fixture.pair(clientB);

        openStream(fixture.service, clientA.fingerprint(), new RecordingObserver<>())
                .onNext(helloEnvelope(clientA, "PC01"));
        openStream(fixture.service, clientB.fingerprint(), new RecordingObserver<>())
                .onNext(helloEnvelope(clientB, "PC02"));

        assertThat(fixture.registry.snapshots())
                .extracting(ClientConnectionSnapshot::status)
                .containsExactlyInAnyOrder(DeviceStatus.ONLINE, DeviceStatus.ONLINE);
        assertThat(fixture.registry.findByDeviceId("PC01")).isEmpty();
        assertThat(fixture.registry.findByDeviceId("PC02")).isEmpty();
    }

    @Test
    void unknownCapabilityDoesNotGrantDeviceCapability() throws Exception {
        Fixture fixture = createFixture("unknown-capability");
        TestClientIdentity client = TestClientIdentity.create("PC01");
        fixture.pair(client);
        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(fixture.service, client.fingerprint(), responses);

        requests.onNext(helloEnvelope(client, "PC01", List.of(NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1), 999));

        assertThat(fixture.registry.find(client.descriptor().clientNetworkIdentityId()))
                .get()
                .satisfies(snapshot -> assertThat(snapshot.capabilities())
                        .containsExactly(DeviceCapability.HEARTBEAT_V1));
    }

    @Test
    void heartbeatDoesNotRecordPersistentConnectionWrites() throws Exception {
        MutableClock clock = new MutableClock(FIXED_NOW);
        CountingNetworkClientConnectionService connectionService = new CountingNetworkClientConnectionService();
        Fixture fixture = createFixture("heartbeat-no-write", clock, new InMemoryMasterNetworkIdentityKeyStore(), connectionService);
        TestClientIdentity client = TestClientIdentity.create("PC01");
        fixture.pair(client);
        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(fixture.service, client.fingerprint(), responses);

        requests.onNext(helloEnvelope(client, "PC01"));
        requests.onNext(heartbeatEnvelope("hb-1"));
        requests.onNext(heartbeatEnvelope("hb-2"));

        assertThat(connectionService.acceptedHelloCount).isEqualTo(1);
    }

    @Test
    void restartPreservesTrustAndAllowsReconnect() throws Exception {
        MutableClock clock = new MutableClock(FIXED_NOW);
        InMemoryMasterNetworkIdentityKeyStore keyStore = new InMemoryMasterNetworkIdentityKeyStore();
        Fixture fixture = createFixture("restart", clock, keyStore);
        TestClientIdentity client = TestClientIdentity.create("PC01");
        fixture.pair(client);

        Fixture restarted = createFixture("restart", clock, keyStore);
        RecordingObserver<MasterEnvelope> responses = new RecordingObserver<>();
        StreamObserver<ClientEnvelope> requests = openStream(restarted.service, client.fingerprint(), responses);
        requests.onNext(helloEnvelope(client, "PC01"));

        assertThat(responses.values().getLast().getConnectionStatus().getStatus())
                .isEqualTo(ConnectionState.CONNECTION_STATE_ONLINE);
        assertThat(restarted.registry.find(client.descriptor().clientNetworkIdentityId())).isPresent();
    }

    @Test
    void tlsTrustManagerRejectsUnpairedAndRevokedCertificates() throws Exception {
        Fixture fixture = createFixture("tls-trust");
        TestClientIdentity client = TestClientIdentity.create("PC01");
        X509Certificate certificate = client.certificate(FIXED_NOW);
        MasterTlsPeerTrustManager trustManager = new MasterTlsPeerTrustManager(fixture.trustStore, fixture.clock);

        assertThatThrownBy(() -> trustManager.checkClientTrusted(new X509Certificate[] {certificate}, "RSA"))
                .isInstanceOf(java.security.cert.CertificateException.class);

        fixture.pair(client);
        trustManager.checkClientTrusted(new X509Certificate[] {certificate}, "RSA");

        fixture.pairingService.revokeClient(client.descriptor().clientNetworkIdentityId());
        assertThatThrownBy(() -> trustManager.checkClientTrusted(new X509Certificate[] {certificate}, "RSA"))
                .isInstanceOf(java.security.cert.CertificateException.class);
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
        return createFixture(name, clock, keyStore, new NoOpNetworkClientConnectionService());
    }

    private Fixture createFixture(
            String name,
            MutableClock clock,
            InMemoryMasterNetworkIdentityKeyStore keyStore,
            NetworkClientConnectionService connectionService) {
        java.nio.file.Path dataDir = tempDir.resolve(name);
        var identityResolver = new MasterNetworkIdentityResolver(
                new MasterNetworkIdentityStore(dataDir),
                keyStore,
                clock);
        var trustStore = new MasterTrustStore(dataDir);
        var pairingService = new MasterPairingService(identityResolver, keyStore, trustStore, clock, new SecureRandom());
        var registry = new ClientConnectionRegistry(clock);
        var service = new MasterNetworkGrpcService(
                new MasterNetworkConnectionAuthenticator(pairingService),
                registry,
                connectionService,
                clock);
        return new Fixture(pairingService, trustStore, registry, service, clock);
    }

    private StreamObserver<ClientEnvelope> openStream(
            MasterNetworkGrpcService service,
            String fingerprint,
            RecordingObserver<MasterEnvelope> responses) throws Exception {
        return Context.current()
                .withValue(MtlsPeerCertificateServerInterceptor.CLIENT_CERTIFICATE_FINGERPRINT, fingerprint)
                .call(() -> service.connect(responses));
    }

    private static ClientEnvelope helloEnvelope(TestClientIdentity client, String deviceId) {
        return helloEnvelope(client, deviceId, List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1), null);
    }

    private static ClientEnvelope helloEnvelope(
            TestClientIdentity client,
            String deviceId,
            List<NetworkCapability> capabilities,
            Integer unknownCapabilityValue) {
        ClientHello.Builder hello = ClientHello.newBuilder()
                .setClientNetworkIdentityId(client.descriptor().clientNetworkIdentityId().toString())
                .setClientInstallationId(client.descriptor().clientInstallationId().toString())
                .setClientPublicKeyFingerprint(client.descriptor().publicKeyFingerprint())
                .setClientPublicKeySubjectPublicKeyInfoBase64(client.descriptor().subjectPublicKeyInfoBase64())
                .setDeviceId(deviceId)
                .setDisplayName(deviceId)
                .setHostname(deviceId)
                .setAgentVersion("0.5.0-test")
                .setSentAtUnixMs(FIXED_NOW.toEpochMilli());
        hello.addAllCapabilities(capabilities);
        if (unknownCapabilityValue != null) {
            hello.addCapabilitiesValue(unknownCapabilityValue);
        }

        return ClientEnvelope.newBuilder()
                .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                .setClientHello(hello.build())
                .build();
    }

    private static ClientEnvelope heartbeatEnvelope(String heartbeatId) {
        return ClientEnvelope.newBuilder()
                .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                .setHeartbeat(Heartbeat.newBuilder()
                        .setHeartbeatId(heartbeatId)
                        .setSentAtUnixMs(FIXED_NOW.toEpochMilli())
                        .build())
                .build();
    }

    private static void assertRejected(
            RecordingObserver<MasterEnvelope> responses,
            String reasonCode) {
        assertThat(responses.completed()).isTrue();
        assertThat(responses.values()).hasSize(1);
        assertThat(responses.values().getFirst().getConnectionStatus().getStatus())
                .isEqualTo(ConnectionState.CONNECTION_STATE_REJECTED);
        assertThat(responses.values().getFirst().getConnectionStatus().getReasonCode())
                .isEqualTo(reasonCode);
    }

    private record Fixture(
            MasterPairingService pairingService,
            MasterTrustStore trustStore,
            ClientConnectionRegistry registry,
            MasterNetworkGrpcService service,
            Clock clock) {

        void pair(TestClientIdentity client) {
            PairingChallenge challenge = pairingService
                    .createPairingChallenge(client.descriptor(), true)
                    .challenge();
            MasterPairingCompletionResult completion = pairingService.completePairing(client.responseTo(challenge));
            assertThat(completion.paired()).isTrue();
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
    }

    private static final class RecordingObserver<T> implements StreamObserver<T> {
        private final List<T> values = new ArrayList<>();
        private Throwable error;
        private boolean completed;

        @Override
        public void onNext(T value) {
            values.add(value);
        }

        @Override
        public void onError(Throwable throwable) {
            error = throwable;
        }

        @Override
        public void onCompleted() {
            completed = true;
        }

        List<T> values() {
            return values;
        }

        boolean completed() {
            return completed;
        }

        Throwable error() {
            return error;
        }
    }

    private static final class NoOpNetworkClientConnectionService implements NetworkClientConnectionService {

        @Override
        public RegisteredNetworkDevice recordAcceptedHello(
                ClientNetworkIdentityDescriptor descriptor,
                ClientHello hello) {
            return null;
        }
    }

    private static final class CountingNetworkClientConnectionService implements NetworkClientConnectionService {

        private int acceptedHelloCount;

        @Override
        public RegisteredNetworkDevice recordAcceptedHello(
                ClientNetworkIdentityDescriptor descriptor,
                ClientHello hello) {
            acceptedHelloCount++;
            return null;
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

            return MasterNetworkSignatureResult.signed(MasterNetworkTransportTest.sign(keyPair.getPrivate(), data));
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
            KeyPair keyPair) {

        static TestClientIdentity create(String deviceName) {
            KeyPair keyPair = generateKeyPair();
            byte[] publicKey = keyPair.getPublic().getEncoded();
            return new TestClientIdentity(
                    new ClientNetworkIdentityDescriptor(
                            UUID.randomUUID(),
                            UUID.randomUUID(),
                            NetworkIdentityCrypto.fingerprint(publicKey),
                            Base64.getEncoder().encodeToString(publicKey)),
                    keyPair);
        }

        String fingerprint() {
            return descriptor.publicKeyFingerprint();
        }

        X509Certificate certificate(Instant now) {
            return NetworkIdentityCertificateFactory.createSelfSigned(
                    keyPair.getPublic(),
                    keyPair.getPrivate(),
                    "test-client",
                    Clock.fixed(now, ZoneId.of("UTC")),
                    new SecureRandom(),
                    KeyPurposeId.id_kp_clientAuth);
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
                    sign(keyPair.getPrivate(), NetworkIdentityCrypto.canonicalResponseBytes(unsigned)));
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
