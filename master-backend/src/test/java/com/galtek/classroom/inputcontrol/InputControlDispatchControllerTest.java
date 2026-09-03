package com.galtek.classroom.inputcontrol;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.never;
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
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.localagent.MasterUnlockAuthorizationResponse;
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
import com.galtek.classroom.operations.BatchOperationRepository;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.nio.file.Files;
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
import java.util.stream.Stream;
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
class InputControlDispatchControllerTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-09-03T12:00:00Z");
    private static final Path DATA_DIR = Path.of(
            "target",
            "test-data",
            "input-control-dispatch-api-" + UUID.randomUUID());

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
        when(localAgentClient.getMasterUnlockAuthorization()).thenReturn(new MasterUnlockAuthorizationResponse(
                "AUTHORIZED",
                true,
                true));
        when(remoteOperationGateway.resultTimeout()).thenReturn(Duration.ofMillis(50));
        when(remoteOperationGateway.dispatch(any(), any(), anyString(), anyString()))
                .thenAnswer(invocation -> successHandle(invocation.getArgument(2), invocation.getArgument(3)));
    }

    @Test
    void lockAndUnlockEndpointsExistAndDispatchSeparateOperationTypes() throws Exception {
        String classroomId = createClassroom("Aula input routes " + id());
        RegisteredClient lockTarget = registerClient(classroomId, "PC01", inputControlCapabilities());
        RegisteredClient unlockTarget = registerClient(classroomId, "PC02", inputControlCapabilities());
        resetEndpointMocks();

        mockMvc.perform(post("/api/classrooms/{id}/input-control/lock", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(lockTarget.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value(OperationType.LOCK_INPUT.name()))
                .andExpect(jsonPath("$.status").value("SUCCESS"));

        verify(localAgentClient).getMasterAuthorization();
        verify(localAgentClient, never()).getMasterUnlockAuthorization();
        verify(remoteOperationGateway).dispatch(
                any(),
                eq(OperationType.LOCK_INPUT),
                anyString(),
                eq(lockTarget.deviceId()));

        resetEndpointMocks();
        mockMvc.perform(post("/api/classrooms/{id}/input-control/unlock", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(unlockTarget.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value(OperationType.UNLOCK_INPUT.name()))
                .andExpect(jsonPath("$.status").value("SUCCESS"));

        verify(localAgentClient).getMasterUnlockAuthorization();
        verify(localAgentClient, never()).getMasterAuthorization();
        verify(remoteOperationGateway).dispatch(
                any(),
                eq(OperationType.UNLOCK_INPUT),
                anyString(),
                eq(unlockTarget.deviceId()));
    }

    @Test
    void acceptsOnlyTargetDeviceIdsAndRejectsGenericTypeEndpoint() throws Exception {
        String classroomId = createClassroom("Aula input strict request " + id());
        resetEndpointMocks();

        for (String endpoint : List.of("lock", "unlock")) {
            expectInvalidRequest(endpoint, classroomId, null);
            expectInvalidRequest(endpoint, classroomId, Map.of("targetDeviceIds", List.of()));
            expectInvalidRequest(endpoint, classroomId, Map.of("targetDeviceIds", List.of(" ")));
            expectInvalidRequest(endpoint, classroomId, Map.of("targetDeviceIds", List.of("device-1", "device-1")));
            expectInvalidRequest(endpoint, classroomId, Map.of("targetDeviceIds", List.of("device-1", " device-1 ")));

            for (String unsupported : List.of(
                    "type",
                    "lock",
                    "unlock",
                    "duration",
                    "timeout",
                    "lease",
                    "message",
                    "keyboardOnly",
                    "mouseOnly",
                    "keyCodes",
                    "force",
                    "accountType",
                    "studentId",
                    "groupId",
                    "command",
                    "arguments",
                    "shell",
                    "payload")) {
                expectInvalidRequest(endpoint, classroomId, Map.of(
                        "targetDeviceIds", List.of("device-1"),
                        unsupported, "not-accepted"));
            }
        }

        mockMvc.perform(post("/api/classrooms/{id}/input-control", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "type", OperationType.LOCK_INPUT.name(),
                                "targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isNotFound());

        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void guardFailuresHappenBeforeStorageBatchOrSend() throws Exception {
        long before = batchCount();
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));
        when(localAgentClient.getMasterUnlockAuthorization()).thenReturn(new MasterUnlockAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true));
        storageState.mark(MasterStorageStatus.UNAVAILABLE, ErrorCode.MASTER_DATABASE_UNAVAILABLE.name());

        mockMvc.perform(post("/api/classrooms/{id}/input-control/lock", "missing")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));
        mockMvc.perform(post("/api/classrooms/{id}/input-control/unlock", "missing")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));

        assertThat(batchCount()).isEqualTo(before);
        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void unlockAuthorizationFailureCreatesNoBatchAndSendsNothing() throws Exception {
        String classroomId = createClassroom("Aula unlock denied " + id());
        resetEndpointMocks();
        long before = batchCount();
        when(localAgentClient.getMasterUnlockAuthorization()).thenReturn(new MasterUnlockAuthorizationResponse(
                "MASTER_BINDING_INVALID",
                false,
                true));

        mockMvc.perform(post("/api/classrooms/{id}/input-control/unlock", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("MASTER_BINDING_INVALID"));

        assertThat(batchCount()).isEqualTo(before);
        verifyNoInteractions(remoteOperationGateway);
        verify(localAgentClient, never()).getMasterAuthorization();
    }

    @Test
    void preflightUsesInputControlCapabilityWithoutSessionAgentRequirement() throws Exception {
        String classroomId = createClassroom("Aula input preflight " + id());
        RegisteredClient ready = registerClient(classroomId, "PC03", inputControlCapabilities());
        RegisteredClient noCapability = registerClient(classroomId, "PC04", List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1));
        RegisteredClient offline = registerClient(classroomId, "PC05", inputControlCapabilities());
        connectionRegistry.markOffline(
                offline.client().descriptor().clientNetworkIdentityId(),
                connectionId(offline.client()),
                "TEST_OFFLINE");
        String unregisteredDeviceId = createUnregisteredDevice(classroomId, "PC06");
        String otherClassroom = createClassroom("Aula other input " + id());
        RegisteredClient other = registerClient(otherClassroom, "PC07", inputControlCapabilities());
        RegisteredClient revoked = registerClient(classroomId, "PC08", inputControlCapabilities());
        pairingService.revokeClient(revoked.client().descriptor().clientNetworkIdentityId());
        resetEndpointMocks();

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/input-control/lock", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(
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
                eq(OperationType.LOCK_INPUT),
                eq(response.get("operationId").asText()),
                eq(ready.deviceId()));
    }

    @Test
    void createsOneLockBatchBeforeFirstSendWithSameOperationIdAndMinimalPayload() throws Exception {
        String classroomId = createClassroom("Aula input lock batch " + id());
        RegisteredClient first = registerClient(classroomId, "PC09", inputControlCapabilities());
        RegisteredClient second = registerClient(classroomId, "PC10", inputControlCapabilities());
        resetEndpointMocks();
        when(remoteOperationGateway.dispatch(any(), eq(OperationType.LOCK_INPUT), anyString(), anyString()))
                .thenAnswer(invocation -> {
                    String operationId = invocation.getArgument(2);
                    assertThat(batchOperationRepository.findById(operationId))
                            .get()
                            .satisfies(operation -> {
                                assertThat(operation.type()).isEqualTo(OperationType.LOCK_INPUT);
                                assertThat(operation.targets()).allSatisfy(target ->
                                        assertThat(target.status().name()).isEqualTo("PENDING"));
                            });
                    return successHandle(operationId, invocation.getArgument(3));
                });

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/input-control/lock", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(first.deviceId(), second.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value(OperationType.LOCK_INPUT.name()))
                .andExpect(jsonPath("$.status").value("SUCCESS"))
                .andReturn());

        ArgumentCaptor<String> operationIds = ArgumentCaptor.forClass(String.class);
        verify(remoteOperationGateway, org.mockito.Mockito.times(2)).dispatch(
                any(),
                eq(OperationType.LOCK_INPUT),
                operationIds.capture(),
                anyString());
        assertThat(operationIds.getAllValues()).containsOnly(response.get("operationId").asText());
        assertMinimalInputPayload(response.get("operationId").asText());
    }

    @Test
    void createsOneUnlockBatchAndPreservesAgentErrorsAndUncertaintyWithoutRetryOrStatusQuery() throws Exception {
        String classroomId = createClassroom("Aula input unlock batch " + id());
        RegisteredClient success = registerClient(classroomId, "PC11", inputControlCapabilities());
        RegisteredClient unlockFailed = registerClient(classroomId, "PC12", inputControlCapabilities());
        RegisteredClient sessionUnavailable = registerClient(classroomId, "PC13", inputControlCapabilities());
        RegisteredClient sessionUnknown = registerClient(classroomId, "PC14", inputControlCapabilities());
        RegisteredClient operationUnknown = registerClient(classroomId, "PC15", inputControlCapabilities());
        resetEndpointMocks();
        whenDispatchSuccess(OperationType.UNLOCK_INPUT, success);
        whenDispatchFailure(OperationType.UNLOCK_INPUT, unlockFailed, ErrorCode.INPUT_UNLOCK_FAILED,
                "Windows did not confirm input unlock on the target device.");
        whenDispatchFailure(OperationType.UNLOCK_INPUT, sessionUnavailable, ErrorCode.SESSION_AGENT_UNAVAILABLE,
                "Session Agent is unavailable on the target device.");
        whenDispatchFailure(OperationType.UNLOCK_INPUT, sessionUnknown, ErrorCode.SESSION_COMMAND_RESULT_UNKNOWN,
                "Session command result is unknown after dispatch.");
        whenDispatchFailure(OperationType.UNLOCK_INPUT, operationUnknown, ErrorCode.OPERATION_RESULT_UNKNOWN,
                "Operation result is unknown after timeout.");

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/input-control/unlock", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(
                                success.deviceId(),
                                unlockFailed.deviceId(),
                                sessionUnavailable.deviceId(),
                                sessionUnknown.deviceId(),
                                operationUnknown.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value(OperationType.UNLOCK_INPUT.name()))
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(1))
                .andExpect(jsonPath("$.failedCount").value(4))
                .andReturn());

        assertThat(target(response, unlockFailed.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.INPUT_UNLOCK_FAILED.name());
        assertThat(target(response, sessionUnavailable.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.SESSION_AGENT_UNAVAILABLE.name());
        assertThat(target(response, sessionUnknown.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.SESSION_COMMAND_RESULT_UNKNOWN.name());
        assertThat(target(response, operationUnknown.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.OPERATION_RESULT_UNKNOWN.name());
        OperationResponse persisted = adminRepository.findOperation(response.get("operationId").asText()).orElseThrow();
        assertThat(persisted.type()).isEqualTo(OperationType.UNLOCK_INPUT.name());
        assertThat(persisted.targetCount()).isEqualTo(5);
        verify(remoteOperationGateway, org.mockito.Mockito.times(5)).dispatch(
                any(),
                eq(OperationType.UNLOCK_INPUT),
                eq(response.get("operationId").asText()),
                anyString());
        verify(remoteOperationGateway, never()).queryStatus(any(), any(), anyString(), anyString());
        assertMinimalInputPayload(response.get("operationId").asText());
    }

    @Test
    void lockPreservesInputLockFailedAndOperationUnknownWithoutRetry() throws Exception {
        String classroomId = createClassroom("Aula input lock errors " + id());
        RegisteredClient lockFailed = registerClient(classroomId, "PC16", inputControlCapabilities());
        RegisteredClient operationUnknown = registerClient(classroomId, "PC17", inputControlCapabilities());
        resetEndpointMocks();
        whenDispatchFailure(OperationType.LOCK_INPUT, lockFailed, ErrorCode.INPUT_LOCK_FAILED,
                "Windows did not confirm input lock on the target device.");
        whenDispatchFailure(OperationType.LOCK_INPUT, operationUnknown, ErrorCode.OPERATION_RESULT_UNKNOWN,
                "Operation result is unknown after timeout.");

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/input-control/lock", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(
                                lockFailed.deviceId(),
                                operationUnknown.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("FAILED"))
                .andExpect(jsonPath("$.successCount").value(0))
                .andReturn());

        assertThat(target(response, lockFailed.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.INPUT_LOCK_FAILED.name());
        assertThat(target(response, operationUnknown.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.OPERATION_RESULT_UNKNOWN.name());
        verify(remoteOperationGateway, org.mockito.Mockito.times(2)).dispatch(
                any(),
                eq(OperationType.LOCK_INPUT),
                eq(response.get("operationId").asText()),
                anyString());
        verify(remoteOperationGateway, never()).queryStatus(any(), any(), anyString(), anyString());
    }

    @Test
    void unlockGuardIsNotUsedByOtherControllersAndServiceDoesNotAuthorizeMasterLicense() throws Exception {
        Path mainJava = Path.of("src", "main", "java");
        try (Stream<Path> files = Files.walk(mainJava)) {
            List<Path> controllerMentions = files
                    .filter(path -> path.getFileName().toString().endsWith("Controller.java"))
                    .filter(path -> {
                        try {
                            return Files.readString(path).contains("MasterUnlockAccessGuard");
                        } catch (Exception exception) {
                            throw new IllegalStateException(exception);
                        }
                    })
                    .toList();
            assertThat(controllerMentions)
                    .extracting(path -> path.getFileName().toString())
                    .containsExactly("InputControlController.java");
        }

        String serviceSource = Files.readString(Path.of(
                "src",
                "main",
                "java",
                "com",
                "galtek",
                "classroom",
                "inputcontrol",
                "InputControlDispatchService.java"));
        assertThat(serviceSource)
                .doesNotContain("LocalAgentClient")
                .doesNotContain("MasterAuthorization")
                .doesNotContain("MasterAccessGuard")
                .doesNotContain("MasterUnlockAccessGuard")
                .doesNotContain("SESSION_AGENT_AVAILABLE")
                .doesNotContain("queryStatus(");
    }

    private void expectInvalidRequest(
            String endpoint,
            String classroomId,
            Map<String, Object> body) throws Exception {
        var builder = post("/api/classrooms/{id}/input-control/{endpoint}", classroomId, endpoint)
                .contentType(MediaType.APPLICATION_JSON);
        if (body != null) {
            builder.content(json(body));
        }
        mockMvc.perform(builder)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));
    }

    private void resetEndpointMocks() {
        reset(localAgentClient, remoteOperationGateway);
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria"));
        when(localAgentClient.getMasterUnlockAuthorization()).thenReturn(new MasterUnlockAuthorizationResponse(
                "AUTHORIZED",
                true,
                true));
        when(remoteOperationGateway.resultTimeout()).thenReturn(Duration.ofMillis(50));
        when(remoteOperationGateway.dispatch(any(), any(), anyString(), anyString()))
                .thenAnswer(invocation -> successHandle(invocation.getArgument(2), invocation.getArgument(3)));
    }

    private void whenDispatchSuccess(OperationType operationType, RegisteredClient client) {
        when(remoteOperationGateway.dispatch(any(), eq(operationType), anyString(), eq(client.deviceId())))
                .thenAnswer(invocation -> successHandle(invocation.getArgument(2), invocation.getArgument(3)));
    }

    private void whenDispatchFailure(
            OperationType operationType,
            RegisteredClient client,
            ErrorCode errorCode,
            String message) {
        when(remoteOperationGateway.dispatch(any(), eq(operationType), anyString(), eq(client.deviceId())))
                .thenAnswer(invocation -> failedHandle(
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

    private String createClassroom(String displayName) throws Exception {
        JsonNode classroom = read(mockMvc.perform(post("/api/classrooms")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("displayName", displayName))))
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

    private List<NetworkCapability> inputControlCapabilities() {
        return List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_INPUT_CONTROL_V1);
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

    private void assertMinimalInputPayload(String operationId) throws Exception {
        String payload = jdbcTemplate.queryForObject(
                "SELECT payload_json FROM batch_operations WHERE operation_id = ?",
                String.class,
                operationId);
        assertThat(objectMapper.readTree(payload).get("schemaVersion").asInt()).isEqualTo(1);
        assertThat(payload)
                .doesNotContain("duration")
                .doesNotContain("message")
                .doesNotContain("session")
                .doesNotContain("thread")
                .doesNotContain("SID")
                .doesNotContain("user");
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
