package com.galtek.classroom.browser;

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
import com.galtek.classroom.browserpolicy.BrowserUrlNormalizer;
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
import com.galtek.classroom.network.v1.OpenUrlOperationParameters;
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
class OpenUrlDispatchControllerTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-09-02T12:00:00Z");
    private static final Path DATA_DIR = Path.of(
            "target",
            "test-data",
            "open-url-dispatch-api-" + UUID.randomUUID());

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
                eq(OperationType.OPEN_URL),
                anyString(),
                anyString(),
                any(OpenUrlOperationParameters.class))).thenAnswer(invocation -> successHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3)));
    }

    @Test
    void rejectsMissingInvalidOrExtraRequestFields() throws Exception {
        mockMvc.perform(post("/api/classrooms/{id}/open-url", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));

        expectInvalidRequest(Map.of("url", " ", "targetDeviceIds", List.of("device-1")));
        expectInvalidRequest(Map.of("url", "https://example.edu", "targetDeviceIds", List.of()));
        expectInvalidRequest(Map.of("url", "https://example.edu", "targetDeviceIds", List.of("device-1", "device-1")));
        expectInvalidRequest(Map.of("url", "https://example.edu", "targetDeviceIds", List.of("device-1", " device-1 ")));
        expectInvalidRequest(Map.of(
                "url", "https://example.edu",
                "targetDeviceIds", List.of("device-1"),
                "browser", "chrome"));
        expectInvalidRequest(Map.of(
                "url", "https://example.edu",
                "targetDeviceIds", List.of("device-1"),
                "accountType", "PRIMARY"));
        expectInvalidRequest(Map.of(
                "url", "https://example.edu",
                "targetDeviceIds", List.of("device-1"),
                "command", "cmd.exe",
                "arguments", "/c calc"));

        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void unsafeUrlIsRejectedGloballyWithoutCreatingBatch() throws Exception {
        String tooLong = "https://example.edu/" + "a".repeat(BrowserUrlNormalizer.MAX_URL_LENGTH);
        List<String> unsafeUrls = List.of(
                "file:///C:/Windows/System32/calc.exe",
                "javascript:alert(1)",
                "data:text/html;base64,PGgxPk5vPC9oMT4=",
                "/relative/path",
                "https://user:pass@example.edu/material",
                "https://example.edu/\nmaterial",
                tooLong);

        for (String unsafeUrl : unsafeUrls) {
            mockMvc.perform(post("/api/classrooms/{id}/open-url", "missing-classroom")
                            .contentType(MediaType.APPLICATION_JSON)
                            .content(json(Map.of(
                                    "url", unsafeUrl,
                                    "targetDeviceIds", List.of("device-1")))))
                    .andExpect(status().isBadRequest())
                    .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_URL.name()));
        }

        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void validHttpAndHttpsDispatchAsOneBatchAndPreserveOriginalUrl() throws Exception {
        String classroomId = createClassroom("Aula open url valid " + id());
        RegisteredClient httpsTarget = registerClient(classroomId, "PC01", openUrlCapabilities());
        RegisteredClient httpTarget = registerClient(classroomId, "PC02", openUrlCapabilities());
        String url = "https://example.edu/material?id=10#section2";

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/open-url", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "url", url,
                                "targetDeviceIds", List.of(httpsTarget.deviceId(), httpTarget.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value(OperationType.OPEN_URL.name()))
                .andExpect(jsonPath("$.status").value("SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(2))
                .andReturn());

        ArgumentCaptor<OpenUrlOperationParameters> params = ArgumentCaptor.forClass(OpenUrlOperationParameters.class);
        ArgumentCaptor<String> operationIds = ArgumentCaptor.forClass(String.class);
        verify(remoteOperationGateway, org.mockito.Mockito.times(2)).dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                operationIds.capture(),
                anyString(),
                params.capture());

        assertThat(operationIds.getAllValues()).containsOnly(response.get("operationId").asText());
        assertThat(params.getAllValues()).extracting(OpenUrlOperationParameters::getUrl)
                .containsExactly(url, url);
        OperationResponse persisted = adminRepository.findOperation(response.get("operationId").asText()).orElseThrow();
        assertThat(persisted.type()).isEqualTo(OperationType.OPEN_URL.name());
        assertThat(persisted.targetCount()).isEqualTo(2);

        RegisteredClient httpOnly = registerClient(classroomId, "PC03", openUrlCapabilities());
        mockMvc.perform(post("/api/classrooms/{id}/open-url", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "url", "http://example.edu/material",
                                "targetDeviceIds", List.of(httpOnly.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("SUCCESS"));
    }

    @Test
    void evaluatesEffectiveAnyPolicyPerTargetBeforeNetworkPreflight() throws Exception {
        String classroomId = createClassroom("Aula open url policy " + id());
        String groupId = createGroup(classroomId, "2", "A");
        String studentId = createStudent(classroomId, groupId, "Ana");
        RegisteredClient groupTarget = registerClient(classroomId, "PC04", openUrlCapabilities());
        RegisteredClient noAssignment = registerClient(classroomId, "PC05", openUrlCapabilities());
        RegisteredClient exactAllow = registerClient(classroomId, "PC06", openUrlCapabilities());
        RegisteredClient exactBlock = registerClient(classroomId, "PC07", openUrlCapabilities());
        assign(studentId, groupTarget.deviceId());

        createNavigationPolicy(classroomId, Map.of(
                "name", "Classroom PRIMARY ignored for open url",
                "mode", "ALLOWLIST",
                "scopeType", "CLASSROOM",
                "accountScope", "PRIMARY"));
        JsonNode classroomAny = createNavigationPolicy(classroomId, Map.of(
                "name", "Classroom any allow school",
                "mode", "ALLOWLIST",
                "scopeType", "CLASSROOM",
                "accountScope", "ANY"));
        createNavigationRule(classroomAny.get("policyId").asText(), Map.of(
                "action", "ALLOW",
                "matchType", "HOST_SUFFIX",
                "pattern", "school.example"));
        JsonNode groupAny = createNavigationPolicy(classroomId, Map.of(
                "name", "Group any blocks school",
                "mode", "BLOCKLIST",
                "scopeType", "GROUP",
                "schoolGroupId", groupId,
                "accountScope", "ANY"));
        createNavigationRule(groupAny.get("policyId").asText(), Map.of(
                "action", "BLOCK",
                "matchType", "HOST_EXACT",
                "pattern", "school.example"));
        JsonNode allowPolicy = createNavigationPolicy(classroomId, Map.of(
                "name", "Exact allow",
                "mode", "ALLOWLIST",
                "scopeType", "DEVICE",
                "deviceId", exactAllow.deviceId(),
                "accountScope", "ANY"));
        createNavigationRule(allowPolicy.get("policyId").asText(), Map.of(
                "action", "ALLOW",
                "matchType", "EXACT_URL",
                "pattern", "https://school.example/video?id=10"));
        JsonNode blockPolicy = createNavigationPolicy(classroomId, Map.of(
                "name", "Exact block",
                "mode", "BLOCKLIST",
                "scopeType", "DEVICE",
                "deviceId", exactBlock.deviceId(),
                "accountScope", "ANY"));
        createNavigationRule(blockPolicy.get("policyId").asText(), Map.of(
                "action", "BLOCK",
                "matchType", "EXACT_URL",
                "pattern", "https://school.example/video?id=10"));

        String url = "https://school.example/video?id=10#lesson";
        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/open-url", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "url", url,
                                "targetDeviceIds", List.of(
                                        groupTarget.deviceId(),
                                        noAssignment.deviceId(),
                                        exactAllow.deviceId(),
                                        exactBlock.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(2))
                .andExpect(jsonPath("$.failedCount").value(2))
                .andReturn());

        assertThat(target(response, groupTarget.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.URL_BLOCKED_BY_POLICY.name());
        assertThat(target(response, exactBlock.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.URL_BLOCKED_BY_POLICY.name());

        ArgumentCaptor<OpenUrlOperationParameters> params = ArgumentCaptor.forClass(OpenUrlOperationParameters.class);
        ArgumentCaptor<String> targetIds = ArgumentCaptor.forClass(String.class);
        verify(remoteOperationGateway, org.mockito.Mockito.times(2)).dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                eq(response.get("operationId").asText()),
                targetIds.capture(),
                params.capture());
        assertThat(targetIds.getAllValues()).containsExactly(noAssignment.deviceId(), exactAllow.deviceId());
        assertThat(params.getAllValues()).extracting(OpenUrlOperationParameters::getUrl)
                .containsExactly(url, url);
    }

    @Test
    void exactUrlDoesNotUseNativeEnforceabilityFailureForOpenUrl() throws Exception {
        String classroomId = createClassroom("Aula exact open url " + id());
        RegisteredClient pc = registerClient(classroomId, "PC08", openUrlCapabilities());
        JsonNode policy = createNavigationPolicy(classroomId, Map.of(
                "name", "Exact url allow",
                "mode", "ALLOWLIST",
                "scopeType", "DEVICE",
                "deviceId", pc.deviceId(),
                "accountScope", "ANY"));
        createNavigationRule(policy.get("policyId").asText(), Map.of(
                "action", "ALLOW",
                "matchType", "EXACT_URL",
                "pattern", "https://school.example/video?id=10"));

        mockMvc.perform(post("/api/classrooms/{id}/open-url", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "url", "https://school.example/video?id=10",
                                "targetDeviceIds", List.of(pc.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("SUCCESS"))
                .andExpect(jsonPath("$.targets[0].errorCode").doesNotExist());

        verify(remoteOperationGateway).dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                anyString(),
                eq(pc.deviceId()),
                any(OpenUrlOperationParameters.class));
    }

    @Test
    void preflightFailuresDoNotCancelReadyTargetsAndPolicyBlockedDoesNotCallGateway() throws Exception {
        String classroomId = createClassroom("Aula open url preflight " + id());
        RegisteredClient ready = registerClient(classroomId, "PC09", openUrlCapabilities());
        RegisteredClient noCapability = registerClient(classroomId, "PC10", List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1));
        RegisteredClient offline = registerClient(classroomId, "PC11", openUrlCapabilities());
        connectionRegistry.markOffline(
                offline.client().descriptor().clientNetworkIdentityId(),
                connectionId(offline.client()),
                "TEST_OFFLINE");
        String unregisteredDeviceId = createUnregisteredDevice(classroomId, "PC12");
        String otherClassroom = createClassroom("Aula otra " + id());
        RegisteredClient other = registerClient(otherClassroom, "PC13", openUrlCapabilities());
        RegisteredClient policyBlocked = registerClient(classroomId, "PC14", openUrlCapabilities());
        JsonNode blockPolicy = createNavigationPolicy(classroomId, Map.of(
                "name", "Block blocked.example",
                "mode", "BLOCKLIST",
                "scopeType", "DEVICE",
                "deviceId", policyBlocked.deviceId(),
                "accountScope", "ANY"));
        createNavigationRule(blockPolicy.get("policyId").asText(), Map.of(
                "action", "BLOCK",
                "matchType", "HOST_EXACT",
                "pattern", "blocked.example"));

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/open-url", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "url", "https://blocked.example/material",
                                "targetDeviceIds", List.of(
                                        ready.deviceId(),
                                        noCapability.deviceId(),
                                        offline.deviceId(),
                                        unregisteredDeviceId,
                                        other.deviceId(),
                                        policyBlocked.deviceId())))))
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
        assertThat(target(response, policyBlocked.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.URL_BLOCKED_BY_POLICY.name());
        verify(remoteOperationGateway).dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                eq(response.get("operationId").asText()),
                eq(ready.deviceId()),
                any(OpenUrlOperationParameters.class));
    }

    @Test
    void mapsAgentResultsAndUnknownsWithoutAutomaticRetry() throws Exception {
        String classroomId = createClassroom("Aula open url results " + id());
        RegisteredClient success = registerClient(classroomId, "PC15", openUrlCapabilities());
        RegisteredClient agentBlocked = registerClient(classroomId, "PC16", openUrlCapabilities());
        RegisteredClient sessionUnavailable = registerClient(classroomId, "PC17", openUrlCapabilities());
        RegisteredClient sessionUnknown = registerClient(classroomId, "PC18", openUrlCapabilities());
        RegisteredClient operationUnknown = registerClient(classroomId, "PC19", openUrlCapabilities());
        reset(remoteOperationGateway);
        when(remoteOperationGateway.resultTimeout()).thenReturn(Duration.ofMillis(50));
        when(remoteOperationGateway.dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                anyString(),
                eq(success.deviceId()),
                any(OpenUrlOperationParameters.class))).thenAnswer(invocation -> successHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3)));
        when(remoteOperationGateway.dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                anyString(),
                eq(agentBlocked.deviceId()),
                any(OpenUrlOperationParameters.class))).thenAnswer(invocation -> failedHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3),
                        ErrorCode.URL_BLOCKED_BY_POLICY,
                        "URL is blocked by applied browser navigation policy."));
        when(remoteOperationGateway.dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                anyString(),
                eq(sessionUnavailable.deviceId()),
                any(OpenUrlOperationParameters.class))).thenAnswer(invocation -> failedHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3),
                        ErrorCode.SESSION_AGENT_UNAVAILABLE,
                        "Session Agent is unavailable on the target device."));
        when(remoteOperationGateway.dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                anyString(),
                eq(sessionUnknown.deviceId()),
                any(OpenUrlOperationParameters.class))).thenAnswer(invocation -> failedHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3),
                        ErrorCode.SESSION_COMMAND_RESULT_UNKNOWN,
                        "Session command result is unknown after dispatch."));
        when(remoteOperationGateway.dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                anyString(),
                eq(operationUnknown.deviceId()),
                any(OpenUrlOperationParameters.class))).thenAnswer(invocation -> failedHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3),
                        ErrorCode.OPERATION_RESULT_UNKNOWN,
                        "Operation result is unknown after timeout."));

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/open-url", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "url", "https://school.example/material",
                                "targetDeviceIds", List.of(
                                        success.deviceId(),
                                        agentBlocked.deviceId(),
                                        sessionUnavailable.deviceId(),
                                        sessionUnknown.deviceId(),
                                        operationUnknown.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(1))
                .andExpect(jsonPath("$.failedCount").value(4))
                .andReturn());

        assertThat(target(response, agentBlocked.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.URL_BLOCKED_BY_POLICY.name());
        assertThat(target(response, sessionUnavailable.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.SESSION_AGENT_UNAVAILABLE.name());
        assertThat(target(response, sessionUnknown.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.SESSION_COMMAND_RESULT_UNKNOWN.name());
        assertThat(target(response, operationUnknown.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.OPERATION_RESULT_UNKNOWN.name());
        verify(remoteOperationGateway, org.mockito.Mockito.times(5)).dispatch(
                any(),
                eq(OperationType.OPEN_URL),
                eq(response.get("operationId").asText()),
                anyString(),
                any(OpenUrlOperationParameters.class));
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

        mockMvc.perform(post("/api/classrooms/{id}/open-url", "missing")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "url", "https://example.edu",
                                "targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));

        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void allFailedTargetsProduceFailedBatch() throws Exception {
        String classroomId = createClassroom("Aula open url failed " + id());
        RegisteredClient blocked = registerClient(classroomId, "PC20", openUrlCapabilities());
        JsonNode blockPolicy = createNavigationPolicy(classroomId, Map.of(
                "name", "Block all host",
                "mode", "BLOCKLIST",
                "scopeType", "DEVICE",
                "deviceId", blocked.deviceId(),
                "accountScope", "ANY"));
        createNavigationRule(blockPolicy.get("policyId").asText(), Map.of(
                "action", "BLOCK",
                "matchType", "HOST_EXACT",
                "pattern", "blocked-all.example"));

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/open-url", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "url", "https://blocked-all.example/material",
                                "targetDeviceIds", List.of(blocked.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("FAILED"))
                .andReturn());

        assertThat(batchOperationRepository.findById(response.get("operationId").asText())).isPresent();
        verifyNoInteractions(remoteOperationGateway);
    }

    private void expectInvalidRequest(Map<String, Object> body) throws Exception {
        mockMvc.perform(post("/api/classrooms/{id}/open-url", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(body)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));
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

    private String createGroup(String classroomId, String grade, String section) throws Exception {
        JsonNode group = read(mockMvc.perform(post("/api/classrooms/{id}/groups", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("grade", grade, "section", section))))
                .andExpect(status().isCreated())
                .andReturn());
        return group.get("groupId").asText();
    }

    private String createStudent(String classroomId, String groupId, String name) throws Exception {
        JsonNode student = read(mockMvc.perform(post("/api/classrooms/{id}/students", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "groupId", groupId,
                                "firstName", name,
                                "lastName", "Alumno"))))
                .andExpect(status().isCreated())
                .andReturn());
        return student.get("studentId").asText();
    }

    private void assign(String studentId, String deviceId) throws Exception {
        mockMvc.perform(post("/api/assignments")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "studentId", studentId,
                                "deviceId", deviceId))))
                .andExpect(status().isCreated());
    }

    private JsonNode createNavigationPolicy(String classroomId, Map<String, Object> body) throws Exception {
        return read(mockMvc.perform(post("/api/classrooms/{id}/browser-policies", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(body)))
                .andExpect(status().isCreated())
                .andReturn());
    }

    private void createNavigationRule(String policyId, Map<String, Object> body) throws Exception {
        mockMvc.perform(post("/api/browser-policies/{id}/rules", policyId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(body)))
                .andExpect(status().isCreated());
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
                OffsetDateTime.parse("2026-09-02T12:00:00Z"),
                java.util.Set.of(),
                null), OffsetDateTime.parse("2026-09-02T12:00:00Z"));
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

    private List<NetworkCapability> openUrlCapabilities() {
        return List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPEN_URL_V1);
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
