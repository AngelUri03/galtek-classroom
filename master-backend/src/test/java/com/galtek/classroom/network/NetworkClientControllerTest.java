package com.galtek.classroom.network;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.reset;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.NetworkCapability;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageState;
import java.nio.file.Path;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.time.Instant;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;

@SpringBootTest(properties = {
        "debug=false",
        "galtek.classroom.master.storage.enabled=true",
        "galtek.classroom.master.storage.busy-timeout-ms=250",
        "logging.level.root=WARN",
        "logging.level.org.springframework=WARN"
})
@AutoConfigureMockMvc
class NetworkClientControllerTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-08-27T16:00:00Z");
    private static final Path DATA_DIR = Path.of(
            "target",
            "test-data",
            "network-client-api-" + UUID.randomUUID());

    @DynamicPropertySource
    static void storageProperties(DynamicPropertyRegistry registry) {
        registry.add(
                "galtek.classroom.master.storage.data-dir",
                () -> DATA_DIR.toAbsolutePath().normalize().toString().replace('\\', '/'));
    }

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private ObjectMapper objectMapper;

    @Autowired
    private MasterPairingService pairingService;

    @Autowired
    private ClientConnectionRegistry connectionRegistry;

    @Autowired
    private MasterStorageState storageState;

    @MockitoBean
    private LocalAgentClient localAgentClient;

    @BeforeEach
    void authorizeMaster() {
        reset(localAgentClient);
        storageState.markReady();
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria"));
    }

    @Test
    void pairedOnlineClientAppearsAvailableForRegistration() throws Exception {
        TestClientIdentity client = pairedClient(UUID.randomUUID());
        connect(client, "PC01");

        JsonNode response = read(mockMvc.perform(get("/api/network/clients"))
                .andExpect(status().isOk())
                .andReturn());
        JsonNode node = clientNode(response, client.descriptor().clientNetworkIdentityId());

        assertThat(node.get("connectionStatus").asText()).isEqualTo("ONLINE");
        assertThat(node.get("registrationStatus").asText()).isEqualTo("AVAILABLE_FOR_REGISTRATION");
        assertThat(node.get("registered").asBoolean()).isFalse();
        assertThat(node.get("deviceId").isNull()).isTrue();
        assertThat(node.get("displayName").asText()).isEqualTo("PC01");
        assertThat(capabilityValues(node))
                .contains(DeviceCapability.HEARTBEAT_V1.name(), DeviceCapability.OPERATION_FRAMEWORK_V1.name());
    }

    @Test
    void registeringClientCreatesDeviceBindingAndRejectsDoubleRegistration() throws Exception {
        String classroomId = createClassroom("Aula registro " + id());
        TestClientIdentity client = pairedClient(UUID.randomUUID());
        connect(client, "PC02");

        JsonNode registered = read(mockMvc.perform(post("/api/classrooms/{classroomId}/devices/register", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "networkIdentityId", client.descriptor().clientNetworkIdentityId().toString(),
                                "displayName", "PC02"))))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.classroomId").value(classroomId))
                .andExpect(jsonPath("$.displayName").value("PC02"))
                .andReturn());

        assertThat(registered.get("deviceId").asText()).isNotBlank();

        JsonNode clients = read(mockMvc.perform(get("/api/network/clients"))
                .andExpect(status().isOk())
                .andReturn());
        JsonNode node = clientNode(clients, client.descriptor().clientNetworkIdentityId());
        assertThat(node.get("registered").asBoolean()).isTrue();
        assertThat(node.get("registrationStatus").asText()).isEqualTo("REGISTERED");
        assertThat(node.get("deviceId").asText()).isEqualTo(registered.get("deviceId").asText());

        mockMvc.perform(post("/api/classrooms/{classroomId}/devices/register", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "networkIdentityId", client.descriptor().clientNetworkIdentityId().toString(),
                                "displayName", "PC02 duplicate"))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value(ErrorCode.NETWORK_IDENTITY_ALREADY_REGISTERED.name()));
    }

    @Test
    void revokedClientCannotRegister() throws Exception {
        String classroomId = createClassroom("Aula revoked " + id());
        TestClientIdentity client = pairedClient(UUID.randomUUID());
        pairingService.revokeClient(client.descriptor().clientNetworkIdentityId());

        mockMvc.perform(post("/api/classrooms/{classroomId}/devices/register", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "networkIdentityId", client.descriptor().clientNetworkIdentityId().toString(),
                                "displayName", "PC03"))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value(ErrorCode.CLIENT_REVOKED.name()));
    }

    @Test
    void differentNetworkIdentityCannotClaimExistingInstallation() throws Exception {
        UUID installationId = UUID.randomUUID();
        String classroomId = createClassroom("Aula instalacion " + id());
        TestClientIdentity first = pairedClient(installationId);
        TestClientIdentity second = pairedClient(installationId);
        connect(first, "PC04");
        connect(second, "PC04-copy");

        mockMvc.perform(post("/api/classrooms/{classroomId}/devices/register", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "networkIdentityId", first.descriptor().clientNetworkIdentityId().toString(),
                                "displayName", "PC04"))))
                .andExpect(status().isCreated());

        mockMvc.perform(post("/api/classrooms/{classroomId}/devices/register", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "networkIdentityId", second.descriptor().clientNetworkIdentityId().toString(),
                                "displayName", "PC04 Copy"))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value(ErrorCode.DEVICE_ALREADY_REGISTERED.name()));
    }

    @Test
    void offlineStateIsReflectedFromMemoryNotHeartbeatWrites() throws Exception {
        TestClientIdentity client = pairedClient(UUID.randomUUID());
        connect(client, "PC05");
        connectionRegistry.markOffline(
                client.descriptor().clientNetworkIdentityId(),
                "connection-" + client.descriptor().clientNetworkIdentityId(),
                "TEST_DISCONNECT");

        JsonNode response = read(mockMvc.perform(get("/api/network/clients"))
                .andExpect(status().isOk())
                .andReturn());
        JsonNode node = clientNode(response, client.descriptor().clientNetworkIdentityId());

        assertThat(node.get("connectionStatus").asText()).isEqualTo("OFFLINE");
    }

    private TestClientIdentity pairedClient(UUID installationId) {
        TestClientIdentity client = TestClientIdentity.create(installationId);
        PairingChallenge challenge = pairingService
                .createPairingChallenge(client.descriptor(), true)
                .challenge();
        MasterPairingCompletionResult completion = pairingService.completePairing(client.responseTo(challenge));
        assertThat(completion.paired()).isTrue();
        return client;
    }

    private void connect(TestClientIdentity client, String displayName) {
        String connectionId = "connection-" + client.descriptor().clientNetworkIdentityId();
        connectionRegistry.markConnecting(client.descriptor(), hello(client, displayName), connectionId);
        connectionRegistry.markOnline(client.descriptor().clientNetworkIdentityId(), connectionId);
    }

    private ClientHello hello(TestClientIdentity client, String displayName) {
        ClientHello.Builder hello = ClientHello.newBuilder()
                .setClientNetworkIdentityId(client.descriptor().clientNetworkIdentityId().toString())
                .setClientInstallationId(client.descriptor().clientInstallationId().toString())
                .setClientPublicKeyFingerprint(client.descriptor().publicKeyFingerprint())
                .setClientPublicKeySubjectPublicKeyInfoBase64(client.descriptor().subjectPublicKeyInfoBase64())
                .setDisplayName(displayName)
                .setHostname(displayName.toLowerCase())
                .setAgentVersion("0.5.0-test")
                .setSentAtUnixMs(FIXED_NOW.toEpochMilli());
        hello.addCapabilities(NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1);
        hello.addCapabilities(NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1);
        hello.addCapabilitiesValue(999);
        return hello.build();
    }

    private String createClassroom(String displayName) throws Exception {
        JsonNode classroom = read(mockMvc.perform(post("/api/classrooms")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("displayName", displayName))))
                .andExpect(status().isCreated())
                .andReturn());
        return classroom.get("classroomId").asText();
    }

    private JsonNode clientNode(JsonNode response, UUID networkIdentityId) {
        for (JsonNode client : response) {
            if (networkIdentityId.toString().equals(client.get("networkIdentityId").asText())) {
                return client;
            }
        }
        throw new AssertionError("Network client not found: " + networkIdentityId);
    }

    private List<String> capabilityValues(JsonNode client) {
        List<String> values = new java.util.ArrayList<>();
        for (JsonNode capability : client.get("capabilities")) {
            values.add(capability.asText());
        }
        return values;
    }

    private JsonNode read(org.springframework.test.web.servlet.MvcResult result) throws Exception {
        return objectMapper.readTree(result.getResponse().getContentAsString());
    }

    private String json(Object value) throws Exception {
        return objectMapper.writeValueAsString(value);
    }

    private String id() {
        return UUID.randomUUID().toString();
    }

    private record TestClientIdentity(
            ClientNetworkIdentityDescriptor descriptor,
            KeyPair keyPair) {

        static TestClientIdentity create(UUID installationId) {
            KeyPair keyPair = generateKeyPair();
            byte[] publicKey = keyPair.getPublic().getEncoded();
            return new TestClientIdentity(
                    new ClientNetworkIdentityDescriptor(
                            UUID.randomUUID(),
                            installationId,
                            NetworkIdentityCrypto.fingerprint(publicKey),
                            Base64.getEncoder().encodeToString(publicKey)),
                    keyPair);
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
