package com.galtek.classroom.network;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.reset;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationKey;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.NetworkCapability;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageState;
import java.time.Duration;
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
import java.util.concurrent.CompletableFuture;
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
import org.mockito.ArgumentCaptor;

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

    @MockitoBean
    private MasterRemoteOperationGateway remoteOperationGateway;

    @BeforeEach
    void authorizeMaster() {
        reset(localAgentClient, remoteOperationGateway);
        storageState.markReady();
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria"));
        when(remoteOperationGateway.resultTimeout()).thenReturn(Duration.ofMillis(50));
        when(remoteOperationGateway.dispatch(
                org.mockito.ArgumentMatchers.any(),
                org.mockito.ArgumentMatchers.any(),
                org.mockito.ArgumentMatchers.anyString(),
                org.mockito.ArgumentMatchers.anyString())).thenAnswer(invocation -> {
                    String deviceId = invocation.getArgument(3);
                    String operationId = invocation.getArgument(2);
                    return java.util.Optional.of(new DispatchHandle(
                            new RemoteOperationKey(deviceId, operationId),
                            CompletableFuture.completedFuture(
                                    RemoteOperationOutcome.success("Agent reported operation success."))));
                });
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

    @Test
    void powerControlEndpointRequiresMasterAuthorizationBeforeClassroomLookup() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));

        mockMvc.perform(post("/api/classrooms/{classroomId}/power-control", "missing-classroom")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "type", "SHUTDOWN",
                                "targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));

        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void powerControlRejectsMalformedAndDuplicateTargets() throws Exception {
        mockMvc.perform(post("/api/classrooms/{classroomId}/power-control", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));

        mockMvc.perform(post("/api/classrooms/{classroomId}/power-control", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "type", "OPEN_URL",
                                "targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));

        mockMvc.perform(post("/api/classrooms/{classroomId}/power-control", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "type", "SHUTDOWN",
                                "targetDeviceIds", List.of("device-1", "device-1")))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));

        mockMvc.perform(post("/api/classrooms/{classroomId}/power-control", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "type", "RESTART",
                                "targetDeviceIds", List.of("device-1"),
                                "force", true))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));
    }

    @Test
    void powerControlDispatchesOnlyReadyRegisteredPairedOnlineTargetsAndPersistsResults() throws Exception {
        String classroomId = createClassroom("Aula power " + id());
        RegisteredClient pc01 = registerPoweredClient(classroomId, "PC01");
        RegisteredClient pc02 = registerPoweredClient(classroomId, "PC02");
        RegisteredClient offline = registerPoweredClient(classroomId, "PC03");
        connectionRegistry.markOffline(
                offline.client().descriptor().clientNetworkIdentityId(),
                connectionId(offline.client()),
                "TEST_OFFLINE");
        RegisteredClient noCapability = registerClient(
                classroomId,
                "PC04",
                List.of(
                        NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                        NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1));
        RegisteredClient revoked = registerPoweredClient(classroomId, "PC05");
        pairingService.revokeClient(revoked.client().descriptor().clientNetworkIdentityId());
        String otherClassroomId = createClassroom("Aula ajena " + id());
        RegisteredClient otherClassroom = registerPoweredClient(otherClassroomId, "PC06");

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{classroomId}/power-control", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "type", "SHUTDOWN",
                                "targetDeviceIds", List.of(
                                        pc01.deviceId(),
                                        pc02.deviceId(),
                                        offline.deviceId(),
                                        noCapability.deviceId(),
                                        revoked.deviceId(),
                                        otherClassroom.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value("SHUTDOWN"))
                .andExpect(jsonPath("$.targetCount").value(6))
                .andExpect(jsonPath("$.successCount").value(2))
                .andExpect(jsonPath("$.failedCount").value(4))
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andReturn());

        String operationId = response.get("operationId").asText();
        assertThat(operationId).isNotBlank();
        assertThat(target(response, pc01.deviceId()).get("status").asText()).isEqualTo("SUCCESS");
        assertThat(target(response, pc02.deviceId()).get("status").asText()).isEqualTo("SUCCESS");
        assertThat(target(response, offline.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_OFFLINE.name());
        assertThat(target(response, noCapability.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.CAPABILITY_NOT_SUPPORTED.name());
        assertThat(target(response, revoked.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.CLIENT_REVOKED.name());
        assertThat(target(response, otherClassroom.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_NOT_FOUND.name());

        ArgumentCaptor<String> operationIds = ArgumentCaptor.forClass(String.class);
        ArgumentCaptor<String> targetIds = ArgumentCaptor.forClass(String.class);
        verify(remoteOperationGateway, org.mockito.Mockito.times(2)).dispatch(
                org.mockito.ArgumentMatchers.any(),
                org.mockito.ArgumentMatchers.eq(OperationType.SHUTDOWN),
                operationIds.capture(),
                targetIds.capture());
        assertThat(operationIds.getAllValues()).containsExactly(operationId, operationId);
        assertThat(targetIds.getAllValues()).containsExactly(pc01.deviceId(), pc02.deviceId());

        mockMvc.perform(get("/api/operations/{id}", operationId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value("SHUTDOWN"))
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.targetCount").value(6))
                .andExpect(jsonPath("$.targets.length()").value(6));
    }

    @Test
    void powerControlAggregatesAllSuccessAndAllFailed() throws Exception {
        String successClassroomId = createClassroom("Aula success " + id());
        RegisteredClient pc01 = registerPoweredClient(successClassroomId, "PC07");
        RegisteredClient pc02 = registerPoweredClient(successClassroomId, "PC08");

        mockMvc.perform(post("/api/classrooms/{classroomId}/power-control", successClassroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "type", "RESTART",
                                "targetDeviceIds", List.of(pc01.deviceId(), pc02.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value("RESTART"))
                .andExpect(jsonPath("$.status").value("SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(2))
                .andExpect(jsonPath("$.failedCount").value(0));

        String failedClassroomId = createClassroom("Aula failed " + id());
        RegisteredClient offline = registerPoweredClient(failedClassroomId, "PC09");
        connectionRegistry.markOffline(
                offline.client().descriptor().clientNetworkIdentityId(),
                connectionId(offline.client()),
                "TEST_OFFLINE");

        mockMvc.perform(post("/api/classrooms/{classroomId}/power-control", failedClassroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "type", "SHUTDOWN",
                                "targetDeviceIds", List.of(offline.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("FAILED"))
                .andExpect(jsonPath("$.successCount").value(0))
                .andExpect(jsonPath("$.failedCount").value(1));
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
        connect(client, displayName, List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1));
    }

    private void connect(TestClientIdentity client, String displayName, List<NetworkCapability> capabilities) {
        String connectionId = "connection-" + client.descriptor().clientNetworkIdentityId();
        connectionRegistry.markConnecting(client.descriptor(), hello(client, displayName, capabilities), connectionId);
        connectionRegistry.markOnline(client.descriptor().clientNetworkIdentityId(), connectionId);
    }

    private ClientHello hello(TestClientIdentity client, String displayName) {
        return hello(client, displayName, List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1));
    }

    private ClientHello hello(
            TestClientIdentity client,
            String displayName,
            List<NetworkCapability> capabilities) {
        ClientHello.Builder hello = ClientHello.newBuilder()
                .setClientNetworkIdentityId(client.descriptor().clientNetworkIdentityId().toString())
                .setClientInstallationId(client.descriptor().clientInstallationId().toString())
                .setClientPublicKeyFingerprint(client.descriptor().publicKeyFingerprint())
                .setClientPublicKeySubjectPublicKeyInfoBase64(client.descriptor().subjectPublicKeyInfoBase64())
                .setDisplayName(displayName)
                .setHostname(displayName.toLowerCase())
                .setAgentVersion("0.5.0-test")
                .setSentAtUnixMs(FIXED_NOW.toEpochMilli());
        hello.addAllCapabilities(capabilities);
        hello.addCapabilitiesValue(999);
        return hello.build();
    }

    private RegisteredClient registerPoweredClient(String classroomId, String displayName) throws Exception {
        return registerClient(classroomId, displayName, List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_POWER_CONTROL_V1));
    }

    private RegisteredClient registerClient(
            String classroomId,
            String displayName,
            List<NetworkCapability> capabilities) throws Exception {
        TestClientIdentity client = pairedClient(UUID.randomUUID());
        connect(client, displayName, capabilities);
        JsonNode registered = read(mockMvc.perform(post("/api/classrooms/{classroomId}/devices/register", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "networkIdentityId", client.descriptor().clientNetworkIdentityId().toString(),
                                "displayName", displayName))))
                .andExpect(status().isCreated())
                .andReturn());
        return new RegisteredClient(client, registered.get("deviceId").asText());
    }

    private String connectionId(TestClientIdentity client) {
        return "connection-" + client.descriptor().clientNetworkIdentityId();
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

    private JsonNode target(JsonNode response, String deviceId) {
        for (JsonNode target : response.get("targets")) {
            if (deviceId.equals(target.get("deviceId").asText())) {
                return target;
            }
        }
        throw new AssertionError("Power target not found: " + deviceId);
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

    private record RegisteredClient(
            TestClientIdentity client,
            String deviceId) {
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
