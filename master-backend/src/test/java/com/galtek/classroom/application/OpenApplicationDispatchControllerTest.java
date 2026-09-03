package com.galtek.classroom.application;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.reset;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.galtek.classroom.admin.AdminDtos.OperationResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientNetworkIdentityDescriptor;
import com.galtek.classroom.network.MasterPairingCompletionResult;
import com.galtek.classroom.network.MasterPairingService;
import com.galtek.classroom.network.MasterRemoteOperationGateway;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationKey;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.NetworkIdentityCrypto;
import com.galtek.classroom.network.PairingChallenge;
import com.galtek.classroom.network.PairingConstants;
import com.galtek.classroom.network.PairingResponse;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.NetworkCapability;
import com.galtek.classroom.network.v1.OpenApplicationOperationParameters;
import com.galtek.classroom.operations.BatchOperationRepository;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.nio.file.Path;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.time.Duration;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.JdbcTemplate;
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
class OpenApplicationDispatchControllerTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-09-03T12:00:00Z");
    private static final Path DATA_DIR = Path.of(
            "target",
            "test-data",
            "open-application-dispatch-api-" + UUID.randomUUID());

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
    private ApplicationCatalogService applicationCatalogService;

    @Autowired
    private MasterPairingService pairingService;

    @Autowired
    private ClientConnectionRegistry connectionRegistry;

    @Autowired
    private MasterStorageState storageState;

    @Autowired
    private MasterAdminRepository adminRepository;

    @Autowired
    private DeviceRepository deviceRepository;

    @Autowired
    private BatchOperationRepository batchOperationRepository;

    @Autowired
    private JdbcTemplate jdbcTemplate;

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
                any(),
                eq(OperationType.OPEN_APPLICATION),
                anyString(),
                anyString(),
                any(OpenApplicationOperationParameters.class))).thenAnswer(invocation -> successHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3)));
    }

    @Test
    void rejectsMissingInvalidOrExtraRequestFields() throws Exception {
        mockMvc.perform(post("/api/classrooms/{id}/open-application", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));

        expectInvalidRequest(Map.of("applicationId", " ", "targetDeviceIds", List.of("device-1")));
        expectInvalidRequest(Map.of("applicationId", "conejito-lector", "targetDeviceIds", List.of()));
        expectInvalidRequest(Map.of("applicationId", "conejito-lector", "targetDeviceIds", List.of(" ")));
        expectInvalidRequest(Map.of(
                "applicationId", "conejito-lector",
                "targetDeviceIds", List.of("device-1", "device-1")));
        expectInvalidRequest(Map.of(
                "applicationId", "conejito-lector",
                "targetDeviceIds", List.of("device-1", " device-1 ")));

        for (String unsupported : List.of(
                "executablePath",
                "appPathExecutableName",
                "launchType",
                "arguments",
                "command",
                "commandLine",
                "workingDirectory",
                "shell",
                "PowerShell",
                "cmd",
                "script",
                "URI",
                "shortcut",
                "registryPath",
                "runAs",
                "elevated",
                "force",
                "timeout",
                "accountType",
                "studentId",
                "groupId",
                "payload")) {
            expectInvalidRequest(Map.of(
                    "applicationId", "conejito-lector",
                    "targetDeviceIds", List.of("device-1"),
                    unsupported, "not-accepted"));
        }

        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void rejectsUnknownInactiveOrUnauthorizedApplicationGloballyWithoutCreatingBatch() throws Exception {
        String appId = createApplication("conejito-" + id(), "Conejito Lector");
        String inactiveAppId = createApplication("archived-" + id(), "Archived App");
        String classroomId = createClassroom("Aula application auth " + id(), List.of(appId));
        String otherClassroom = createClassroom("Aula other app auth " + id(), List.of());
        jdbcTemplate.update("UPDATE application_definitions SET active = 0 WHERE application_id = ?", inactiveAppId);
        long before = batchCount();

        expectApplicationUnavailable(classroomId, "missing-" + id());
        expectApplicationUnavailable(classroomId, inactiveAppId);
        expectApplicationUnavailable(otherClassroom, appId);
        expectApplicationUnavailable(classroomId, "Conejito Lector");

        assertThat(batchCount()).isEqualTo(before);
        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void dispatchesAuthorizedApplicationAsOneBatchWithPersistedApplicationIdAndPayload() throws Exception {
        String applicationId = createApplication("conejito-" + id(), "Conejito Lector");
        String classroomId = createClassroom("Aula open application " + id(), List.of(applicationId));
        RegisteredClient first = registerClient(classroomId, "PC01", openApplicationCapabilities());
        RegisteredClient second = registerClient(classroomId, "PC02", openApplicationCapabilities());

        when(remoteOperationGateway.dispatch(
                any(),
                eq(OperationType.OPEN_APPLICATION),
                anyString(),
                anyString(),
                any(OpenApplicationOperationParameters.class))).thenAnswer(invocation -> {
                    String operationId = invocation.getArgument(2);
                    assertThat(batchOperationRepository.findById(operationId))
                            .get()
                            .satisfies(operation -> {
                                assertThat(operation.type()).isEqualTo(OperationType.OPEN_APPLICATION);
                                assertThat(operation.targets()).allSatisfy(target ->
                                        assertThat(target.status().name()).isEqualTo("PENDING"));
                            });
                    return successHandle(operationId, invocation.getArgument(3));
                });

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/open-application", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "applicationId", applicationId,
                                "targetDeviceIds", List.of(first.deviceId(), second.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value(OperationType.OPEN_APPLICATION.name()))
                .andExpect(jsonPath("$.status").value("SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(2))
                .andExpect(jsonPath("$.failedCount").value(0))
                .andReturn());

        ArgumentCaptor<OpenApplicationOperationParameters> params =
                ArgumentCaptor.forClass(OpenApplicationOperationParameters.class);
        ArgumentCaptor<String> operationIds = ArgumentCaptor.forClass(String.class);
        verify(remoteOperationGateway, org.mockito.Mockito.times(2)).dispatch(
                any(),
                eq(OperationType.OPEN_APPLICATION),
                operationIds.capture(),
                anyString(),
                params.capture());

        assertThat(operationIds.getAllValues()).containsOnly(response.get("operationId").asText());
        assertThat(params.getAllValues()).extracting(OpenApplicationOperationParameters::getApplicationId)
                .containsExactly(applicationId, applicationId);

        OperationResponse persisted = adminRepository.findOperation(response.get("operationId").asText()).orElseThrow();
        assertThat(persisted.type()).isEqualTo(OperationType.OPEN_APPLICATION.name());
        assertThat(persisted.targetCount()).isEqualTo(2);
        String payload = jdbcTemplate.queryForObject(
                "SELECT payload_json FROM batch_operations WHERE operation_id = ?",
                String.class,
                response.get("operationId").asText());
        assertThat(objectMapper.readTree(payload).get("applicationId").asText()).isEqualTo(applicationId);
        assertThat(payload)
                .doesNotContain("displayName")
                .doesNotContain("path")
                .doesNotContain("launchType")
                .doesNotContain("arguments")
                .doesNotContain("command")
                .doesNotContain("workingDirectory");
    }

    @Test
    void preflightFailuresDoNotCancelReadyTargetsAndSessionAgentCapabilityIsNotRequired() throws Exception {
        String applicationId = createApplication("scratch-" + id(), "Scratch");
        String classroomId = createClassroom("Aula open app preflight " + id(), List.of(applicationId));
        RegisteredClient ready = registerClient(classroomId, "PC03", openApplicationCapabilities());
        RegisteredClient noCapability = registerClient(classroomId, "PC04", List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1));
        RegisteredClient offline = registerClient(classroomId, "PC05", openApplicationCapabilities());
        connectionRegistry.markOffline(
                offline.client().descriptor().clientNetworkIdentityId(),
                connectionId(offline.client()),
                "TEST_OFFLINE");
        String unregisteredDeviceId = createUnregisteredDevice(classroomId, "PC06");
        String otherClassroom = createClassroom("Aula other preflight " + id(), List.of(applicationId));
        RegisteredClient other = registerClient(otherClassroom, "PC07", openApplicationCapabilities());
        RegisteredClient revoked = registerClient(classroomId, "PC08", openApplicationCapabilities());
        pairingService.revokeClient(revoked.client().descriptor().clientNetworkIdentityId());

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/open-application", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "applicationId", applicationId,
                                "targetDeviceIds", List.of(
                                        ready.deviceId(),
                                        noCapability.deviceId(),
                                        offline.deviceId(),
                                        unregisteredDeviceId,
                                        other.deviceId(),
                                        revoked.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(1))
                .andExpect(jsonPath("$.failedCount").value(5))
                .andReturn());

        assertThat(target(response, noCapability.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.CAPABILITY_NOT_SUPPORTED.name());
        assertThat(target(response, offline.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_OFFLINE.name());
        assertThat(target(response, unregisteredDeviceId).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_NOT_REGISTERED.name());
        assertThat(target(response, other.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_NOT_FOUND.name());
        assertThat(target(response, revoked.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.CLIENT_REVOKED.name());
        verify(remoteOperationGateway).dispatch(
                any(),
                eq(OperationType.OPEN_APPLICATION),
                eq(response.get("operationId").asText()),
                eq(ready.deviceId()),
                any(OpenApplicationOperationParameters.class));
    }

    @Test
    void mapsAgentApplicationResultsAndUnknownsWithoutAutomaticRetry() throws Exception {
        String applicationId = createApplication("word-" + id(), "Word");
        String classroomId = createClassroom("Aula open app results " + id(), List.of(applicationId));
        RegisteredClient success = registerClient(classroomId, "PC09", openApplicationCapabilities());
        RegisteredClient missingBinding = registerClient(classroomId, "PC10", openApplicationCapabilities());
        RegisteredClient executableMissing = registerClient(classroomId, "PC11", openApplicationCapabilities());
        RegisteredClient sessionUnavailable = registerClient(classroomId, "PC12", openApplicationCapabilities());
        RegisteredClient sessionUnknown = registerClient(classroomId, "PC13", openApplicationCapabilities());
        RegisteredClient operationUnknown = registerClient(classroomId, "PC14", openApplicationCapabilities());
        reset(remoteOperationGateway);
        when(remoteOperationGateway.resultTimeout()).thenReturn(Duration.ofMillis(50));
        whenDispatchSuccess(success);
        whenDispatchFailure(missingBinding, ErrorCode.APPLICATION_BINDING_NOT_FOUND,
                "Application binding was not found on the target device.");
        whenDispatchFailure(executableMissing, ErrorCode.APPLICATION_EXECUTABLE_NOT_FOUND,
                "Application executable was not found on the target device.");
        whenDispatchFailure(sessionUnavailable, ErrorCode.SESSION_AGENT_UNAVAILABLE,
                "Session Agent is unavailable on the target device.");
        whenDispatchFailure(sessionUnknown, ErrorCode.SESSION_COMMAND_RESULT_UNKNOWN,
                "Session command result is unknown after dispatch.");
        whenDispatchFailure(operationUnknown, ErrorCode.OPERATION_RESULT_UNKNOWN,
                "Operation result is unknown after timeout.");

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/open-application", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "applicationId", applicationId,
                                "targetDeviceIds", List.of(
                                        success.deviceId(),
                                        missingBinding.deviceId(),
                                        executableMissing.deviceId(),
                                        sessionUnavailable.deviceId(),
                                        sessionUnknown.deviceId(),
                                        operationUnknown.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(1))
                .andExpect(jsonPath("$.failedCount").value(5))
                .andReturn());

        assertThat(target(response, missingBinding.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.APPLICATION_BINDING_NOT_FOUND.name());
        assertThat(target(response, executableMissing.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.APPLICATION_EXECUTABLE_NOT_FOUND.name());
        assertThat(target(response, sessionUnavailable.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.SESSION_AGENT_UNAVAILABLE.name());
        assertThat(target(response, sessionUnknown.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.SESSION_COMMAND_RESULT_UNKNOWN.name());
        assertThat(target(response, operationUnknown.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.OPERATION_RESULT_UNKNOWN.name());
        verify(remoteOperationGateway, org.mockito.Mockito.times(6)).dispatch(
                any(),
                eq(OperationType.OPEN_APPLICATION),
                eq(response.get("operationId").asText()),
                anyString(),
                any(OpenApplicationOperationParameters.class));
    }

    @Test
    void allFailedTargetsProduceFailedBatchWithoutSend() throws Exception {
        String applicationId = createApplication("blocked-all-" + id(), "Blocked All");
        String classroomId = createClassroom("Aula open app failed " + id(), List.of(applicationId));
        RegisteredClient noCapability = registerClient(classroomId, "PC15", List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1));

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/open-application", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "applicationId", applicationId,
                                "targetDeviceIds", List.of(noCapability.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("FAILED"))
                .andReturn());

        assertThat(batchOperationRepository.findById(response.get("operationId").asText())).isPresent();
        verify(remoteOperationGateway, org.mockito.Mockito.never()).dispatch(
                any(),
                eq(OperationType.OPEN_APPLICATION),
                anyString(),
                anyString(),
                any(OpenApplicationOperationParameters.class));
    }

    @Test
    void authorizationRunsBeforeSchoolStorageAccess() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));
        storageState.mark(MasterStorageStatus.UNAVAILABLE, ErrorCode.MASTER_DATABASE_UNAVAILABLE.name());

        mockMvc.perform(post("/api/classrooms/{id}/open-application", "missing")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "applicationId", "conejito-lector",
                                "targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));

        verifyNoInteractions(remoteOperationGateway);
    }

    private void expectInvalidRequest(Map<String, Object> body) throws Exception {
        mockMvc.perform(post("/api/classrooms/{id}/open-application", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(body)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));
    }

    private void expectApplicationUnavailable(String classroomId, String applicationId) throws Exception {
        mockMvc.perform(post("/api/classrooms/{id}/open-application", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "applicationId", applicationId,
                                "targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value(ErrorCode.APPLICATION_NOT_ALLOWED.name()));
    }

    private void whenDispatchSuccess(RegisteredClient client) {
        when(remoteOperationGateway.dispatch(
                any(),
                eq(OperationType.OPEN_APPLICATION),
                anyString(),
                eq(client.deviceId()),
                any(OpenApplicationOperationParameters.class))).thenAnswer(invocation -> successHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3)));
    }

    private void whenDispatchFailure(
            RegisteredClient client,
            ErrorCode errorCode,
            String message) {
        when(remoteOperationGateway.dispatch(
                any(),
                eq(OperationType.OPEN_APPLICATION),
                anyString(),
                eq(client.deviceId()),
                any(OpenApplicationOperationParameters.class))).thenAnswer(invocation -> failedHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3),
                        errorCode,
                        message));
    }

    private java.util.Optional<DispatchHandle> successHandle(String operationId, String deviceId) {
        return java.util.Optional.of(new DispatchHandle(
                new RemoteOperationKey(deviceId, operationId),
                CompletableFuture.completedFuture(
                        RemoteOperationOutcome.success("Agent reported operation success."))));
    }

    private java.util.Optional<DispatchHandle> failedHandle(
            String operationId,
            String deviceId,
            ErrorCode errorCode,
            String message) {
        return java.util.Optional.of(new DispatchHandle(
                new RemoteOperationKey(deviceId, operationId),
                CompletableFuture.completedFuture(
                        RemoteOperationOutcome.failed(errorCode, message))));
    }

    private String createApplication(String applicationId, String displayName) {
        applicationCatalogService.create(new ApplicationDefinition(
                applicationId,
                displayName,
                ApplicationType.EDUCATIONAL_CONTENT,
                ApplicationAvailability.REQUIRED,
                LaunchPolicy.ALLOWED));
        return applicationId;
    }

    private String createClassroom(String displayName, List<String> authorizedApplicationIds) throws Exception {
        Map<String, Object> body = authorizedApplicationIds.isEmpty()
                ? Map.of("displayName", displayName)
                : Map.of("displayName", displayName, "authorizedApplicationIds", authorizedApplicationIds);
        JsonNode classroom = read(mockMvc.perform(post("/api/classrooms")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(body)))
                .andExpect(status().isCreated())
                .andReturn());
        return classroom.get("classroomId").asText();
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

    private String createUnregisteredDevice(String classroomId, String displayName) {
        String deviceId = id();
        deviceRepository.create(classroomId, new Device(
                deviceId,
                id(),
                displayName,
                displayName.toLowerCase(),
                DeviceStatus.OFFLINE,
                OffsetDateTime.parse("2026-09-03T12:00:00Z"),
                java.util.Set.of(),
                null), OffsetDateTime.parse("2026-09-03T12:00:00Z"));
        return deviceId;
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

    private void connect(
            TestClientIdentity client,
            String displayName,
            List<NetworkCapability> capabilities) {
        String connectionId = connectionId(client);
        connectionRegistry.markConnecting(client.descriptor(), hello(client, displayName, capabilities), connectionId);
        connectionRegistry.markOnline(client.descriptor().clientNetworkIdentityId(), connectionId);
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
        return hello.build();
    }

    private List<NetworkCapability> openApplicationCapabilities() {
        return List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPEN_APPLICATION_V1);
    }

    private String connectionId(TestClientIdentity client) {
        return "connection-" + client.descriptor().clientNetworkIdentityId();
    }

    private JsonNode target(JsonNode response, String deviceId) {
        for (JsonNode target : response.get("targets")) {
            if (deviceId.equals(target.get("deviceId").asText())) {
                return target;
            }
        }
        throw new AssertionError("Target not found: " + deviceId);
    }

    private JsonNode read(org.springframework.test.web.servlet.MvcResult result) throws Exception {
        return objectMapper.readTree(result.getResponse().getContentAsString());
    }

    private String json(Object value) throws Exception {
        return objectMapper.writeValueAsString(value);
    }

    private long batchCount() {
        Long count = jdbcTemplate.queryForObject("SELECT COUNT(*) FROM batch_operations", Long.class);
        return count == null ? 0 : count;
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
