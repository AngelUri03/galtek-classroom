package com.galtek.classroom.windows;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.never;
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
import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
import com.galtek.classroom.network.v1.NetworkCapability;
import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchOperationRepository;
import com.galtek.classroom.operations.BatchOperationService;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationPayload;
import com.galtek.classroom.operations.OperationTarget;
import com.galtek.classroom.operations.OperationTargetType;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
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
class ManagedAccountSwitchDispatchControllerTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-09-06T12:00:00Z");
    private static final Path DATA_DIR = Path.of(
            "target",
            "test-data",
            "managed-account-switch-dispatch-api-" + UUID.randomUUID());

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
    private BatchOperationService batchOperationService;

    @Autowired
    private JdbcTemplate jdbcTemplate;

    @MockitoBean
    private LocalAgentClient localAgentClient;

    @MockitoBean
    private MasterRemoteOperationGateway remoteOperationGateway;

    @BeforeEach
    void authorizeMaster() {
        resetEndpointMocks();
    }

    @Test
    void acceptsPrimaryAndSecondaryOnlyWithStrictExplicitTargets() throws Exception {
        String classroomId = createClassroom("Aula managed switch validation " + id());
        RegisteredClient primaryTarget = registerClient(classroomId, "PC01", switchCapabilities());
        RegisteredClient secondaryTarget = registerClient(classroomId, "PC02", switchCapabilities());
        resetEndpointMocks();
        whenSnapshot(primaryTarget, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_PRIMARY_ACTIVE);
        whenSnapshot(secondaryTarget, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);

        mockMvc.perform(post("/api/classrooms/{id}/managed-accounts/switch", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetAccountId", "PRIMARY",
                                "targetDeviceIds", List.of(primaryTarget.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.targetAccountId").value("PRIMARY"))
                .andExpect(jsonPath("$.summary.noChange").value(1));

        mockMvc.perform(post("/api/classrooms/{id}/managed-accounts/switch", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetAccountId", "SECONDARY",
                                "targetDeviceIds", List.of(secondaryTarget.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.targetAccountId").value("SECONDARY"))
                .andExpect(jsonPath("$.summary.noChange").value(1));

        expectInvalidRequest(classroomId, null);
        expectInvalidRequest(classroomId, Map.of("targetAccountId", "PRIMARY", "targetDeviceIds", List.of()));
        expectInvalidRequest(classroomId, Map.of("targetAccountId", "UNSPECIFIED", "targetDeviceIds", List.of("d1")));
        expectInvalidRequest(classroomId, Map.of("targetAccountId", "TERTIARY", "targetDeviceIds", List.of("d1")));
        expectInvalidRequest(classroomId, Map.of("targetAccountId", "PRIMARY", "targetDeviceIds", List.of(" ")));
        expectInvalidRequest(classroomId, Map.of(
                "targetAccountId", "PRIMARY",
                "targetDeviceIds", List.of("d1", " d1 ")));

        for (String unsupported : List.of(
                "sourceAccountId",
                "username",
                "password",
                "SID",
                "credentialId",
                "vaultSessionToken",
                "sessionId",
                "force",
                "timeout",
                "retry",
                "groupId",
                "studentId",
                "allDevices",
                "command",
                "args",
                "payload")) {
            expectInvalidRequest(classroomId, Map.of(
                    "targetAccountId", "PRIMARY",
                    "targetDeviceIds", List.of("d1"),
                    unsupported, "not-accepted"));
        }
    }

    @Test
    void authorizationRunsBeforeSchoolStorageAccessOrRemoteWork() throws Exception {
        long before = batchCount();
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));
        storageState.mark(MasterStorageStatus.UNAVAILABLE, ErrorCode.MASTER_DATABASE_UNAVAILABLE.name());

        mockMvc.perform(post("/api/classrooms/{id}/managed-accounts/switch", "missing")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetAccountId", "PRIMARY",
                                "targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));

        assertThat(batchCount()).isEqualTo(before);
        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void persistsBatchBeforeSnapshotAndNoChangeNeverCallsSwitch() throws Exception {
        String classroomId = createClassroom("Aula managed switch no change " + id());
        RegisteredClient target = registerClient(classroomId, "PC03", switchCapabilities());
        resetEndpointMocks();
        when(remoteOperationGateway.getWindowsSessionState(any(), anyString(), eq(target.deviceId())))
                .thenAnswer(invocation -> {
                    assertThat(jdbcTemplate.queryForObject(
                            "SELECT COUNT(*) FROM batch_operations WHERE operation_type = 'SWITCH_MANAGED_ACCOUNT'",
                            Long.class))
                            .isGreaterThan(0);
                    return snapshotHandle(
                            invocation.getArgument(1),
                            invocation.getArgument(2),
                            com.galtek.classroom.network.v1.WindowsSessionState
                                    .WINDOWS_SESSION_STATE_PRIMARY_ACTIVE);
                });

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/managed-accounts/switch", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetAccountId", "PRIMARY",
                                "targetDeviceIds", List.of(target.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value(OperationType.SWITCH_MANAGED_ACCOUNT.name()))
                .andExpect(jsonPath("$.status").value("SUCCESS"))
                .andExpect(jsonPath("$.summary.noChange").value(1))
                .andExpect(jsonPath("$.summary.success").value(0))
                .andExpect(jsonPath("$.summary.failed").value(0))
                .andExpect(jsonPath("$.targets[0].status").value(TargetExecutionStatus.NO_CHANGE.name()))
                .andReturn());

        verify(remoteOperationGateway).getWindowsSessionState(any(), anyString(), eq(target.deviceId()));
        verify(remoteOperationGateway, never()).switchManagedAccount(
                any(),
                anyString(),
                anyString(),
                any(ManagedWindowsAccountId.class));
        OperationResponse persisted = adminRepository.findOperation(response.get("operationId").asText()).orElseThrow();
        assertThat(persisted.targets().getFirst().status()).isEqualTo(TargetExecutionStatus.NO_CHANGE.name());
        assertThat(mockMvc.perform(get("/api/operations/{id}/retryable-targets", response.get("operationId").asText()))
                .andExpect(status().isOk())
                .andReturn()
                .getResponse()
                .getContentAsString()).isEqualTo("[]");
    }

    @Test
    void preflightFailuresDoNotCancelReadyTargetsAndSessionAgentIsNotRequired() throws Exception {
        String classroomId = createClassroom("Aula managed switch preflight " + id());
        RegisteredClient ready = registerClient(classroomId, "PC04", switchCapabilitiesWithoutSessionAgent());
        RegisteredClient missingState = registerClient(classroomId, "PC05", List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_WINDOWS_SESSION_SWITCH_V1));
        RegisteredClient missingSwitch = registerClient(classroomId, "PC06", List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_WINDOWS_SESSION_STATE_V1));
        RegisteredClient offline = registerClient(classroomId, "PC07", switchCapabilities());
        connectionRegistry.markOffline(
                offline.client().descriptor().clientNetworkIdentityId(),
                connectionId(offline.client()),
                "TEST_OFFLINE");
        String unregisteredDeviceId = createUnregisteredDevice(classroomId, "PC08");
        String otherClassroom = createClassroom("Aula managed switch other " + id());
        RegisteredClient other = registerClient(otherClassroom, "PC09", switchCapabilities());
        RegisteredClient revoked = registerClient(classroomId, "PC10", switchCapabilities());
        pairingService.revokeClient(revoked.client().descriptor().clientNetworkIdentityId());
        resetEndpointMocks();
        whenSnapshot(ready, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);
        whenSwitchSuccess(ready);

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/managed-accounts/switch", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetAccountId", "PRIMARY",
                                "targetDeviceIds", List.of(
                                        ready.deviceId(),
                                        missingState.deviceId(),
                                        missingSwitch.deviceId(),
                                        offline.deviceId(),
                                        unregisteredDeviceId,
                                        other.deviceId(),
                                        revoked.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.summary.success").value(1))
                .andExpect(jsonPath("$.summary.failed").value(6))
                .andReturn());

        assertThat(target(response, missingState.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.CAPABILITY_NOT_SUPPORTED.name());
        assertThat(target(response, missingSwitch.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.CAPABILITY_NOT_SUPPORTED.name());
        assertThat(target(response, offline.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_OFFLINE.name());
        assertThat(target(response, unregisteredDeviceId).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_NOT_REGISTERED.name());
        assertThat(target(response, other.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_NOT_FOUND.name());
        assertThat(target(response, revoked.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.CLIENT_REVOKED.name());
        verify(remoteOperationGateway).switchManagedAccount(
                any(),
                anyString(),
                eq(ready.deviceId()),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY));
    }

    @Test
    void snapshotPlanningBlocksOtherAndUnknownAndNeverSendsSwitch() throws Exception {
        String classroomId = createClassroom("Aula managed switch snapshot block " + id());
        RegisteredClient other = registerClient(classroomId, "PC11", switchCapabilities());
        RegisteredClient unknown = registerClient(classroomId, "PC12", switchCapabilities());
        RegisteredClient snapshotFailed = registerClient(classroomId, "PC13", switchCapabilities());
        resetEndpointMocks();
        whenSnapshot(other, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_OTHER_SESSION_ACTIVE);
        whenSnapshot(unknown, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_UNKNOWN);
        when(remoteOperationGateway.getWindowsSessionState(any(), anyString(), eq(snapshotFailed.deviceId())))
                .thenAnswer(invocation -> failedHandle(
                        invocation.getArgument(1),
                        invocation.getArgument(2),
                        ErrorCode.OPERATION_RESULT_UNKNOWN,
                        "Operation result is unknown after timeout."));

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/managed-accounts/switch", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetAccountId", "SECONDARY",
                                "targetDeviceIds", List.of(other.deviceId(), unknown.deviceId(), snapshotFailed.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("FAILED"))
                .andReturn());

        assertThat(target(response, other.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.WINDOWS_SESSION_CHANGED.name());
        assertThat(target(response, unknown.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.WINDOWS_SESSION_UNKNOWN.name());
        assertThat(target(response, snapshotFailed.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.OPERATION_RESULT_UNKNOWN.name());
        verify(remoteOperationGateway, never()).switchManagedAccount(
                any(),
                anyString(),
                anyString(),
                any(ManagedWindowsAccountId.class));
    }

    @Test
    void logonPlanAndSwitchPlanBothMutateThroughSwitchAndPreserveAgentErrors() throws Exception {
        String classroomId = createClassroom("Aula managed switch results " + id());
        RegisteredClient noSession = registerClient(classroomId, "PC14", switchCapabilities());
        RegisteredClient opposite = registerClient(classroomId, "PC15", switchCapabilities());
        RegisteredClient missingAccount = registerClient(classroomId, "PC16", switchCapabilities());
        RegisteredClient missingCredential = registerClient(classroomId, "PC17", switchCapabilities());
        RegisteredClient providerUnavailable = registerClient(classroomId, "PC18", switchCapabilities());
        RegisteredClient unknownResult = registerClient(classroomId, "PC19", switchCapabilities());
        resetEndpointMocks();
        whenSnapshot(noSession, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_NO_SESSION);
        whenSnapshot(opposite, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);
        whenSnapshot(missingAccount, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_NO_SESSION);
        whenSnapshot(missingCredential, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);
        whenSnapshot(providerUnavailable, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);
        whenSnapshot(unknownResult, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);
        whenSwitchSuccess(noSession);
        whenSwitchSuccess(opposite);
        whenSwitchFailure(missingAccount, ErrorCode.ACCOUNT_NOT_CONFIGURED);
        whenSwitchFailure(missingCredential, ErrorCode.MANAGED_CREDENTIAL_NOT_CONFIGURED);
        whenSwitchFailure(providerUnavailable, ErrorCode.CREDENTIAL_PROVIDER_UNAVAILABLE);
        whenSwitchFailure(unknownResult, ErrorCode.OPERATION_RESULT_UNKNOWN);

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/managed-accounts/switch", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetAccountId", "PRIMARY",
                                "targetDeviceIds", List.of(
                                        noSession.deviceId(),
                                        opposite.deviceId(),
                                        missingAccount.deviceId(),
                                        missingCredential.deviceId(),
                                        providerUnavailable.deviceId(),
                                        unknownResult.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.summary.success").value(2))
                .andExpect(jsonPath("$.summary.failed").value(4))
                .andReturn());

        assertThat(target(response, missingAccount.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.ACCOUNT_NOT_CONFIGURED.name());
        assertThat(target(response, missingCredential.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.MANAGED_CREDENTIAL_NOT_CONFIGURED.name());
        assertThat(target(response, providerUnavailable.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.CREDENTIAL_PROVIDER_UNAVAILABLE.name());
        assertThat(target(response, unknownResult.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.OPERATION_RESULT_UNKNOWN.name());

        ArgumentCaptor<String> snapshotOperationIds = ArgumentCaptor.forClass(String.class);
        ArgumentCaptor<String> switchOperationIds = ArgumentCaptor.forClass(String.class);
        verify(remoteOperationGateway, org.mockito.Mockito.times(6)).getWindowsSessionState(
                any(),
                snapshotOperationIds.capture(),
                anyString());
        verify(remoteOperationGateway, org.mockito.Mockito.times(6)).switchManagedAccount(
                any(),
                switchOperationIds.capture(),
                anyString(),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY));
        assertThat(snapshotOperationIds.getAllValues()).doesNotContain(response.get("operationId").asText());
        assertThat(switchOperationIds.getAllValues()).doesNotContain(response.get("operationId").asText());
        assertThat(snapshotOperationIds.getAllValues()).doesNotContainAnyElementsOf(switchOperationIds.getAllValues());
    }

    @Test
    void persistedOperationsAndRetryableTargetsReadBackCorrectly() throws Exception {
        String classroomId = createClassroom("Aula managed switch readback " + id());
        RegisteredClient noChange = registerClient(classroomId, "PC20", switchCapabilities());
        RegisteredClient success = registerClient(classroomId, "PC21", switchCapabilities());
        RegisteredClient retryable = registerClient(classroomId, "PC22", switchCapabilities());
        RegisteredClient notRetryable = registerClient(classroomId, "PC23", switchCapabilities());
        resetEndpointMocks();
        whenSnapshot(noChange, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_PRIMARY_ACTIVE);
        whenSnapshot(success, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);
        whenSnapshot(retryable, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);
        whenSnapshot(notRetryable, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);
        whenSwitchSuccess(success);
        whenSwitchFailure(retryable, ErrorCode.WINDOWS_LOGON_FAILED);
        whenSwitchFailure(notRetryable, ErrorCode.MANAGED_CREDENTIAL_NOT_CONFIGURED);

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/managed-accounts/switch", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetAccountId", "PRIMARY",
                                "targetDeviceIds", List.of(
                                        noChange.deviceId(),
                                        success.deviceId(),
                                        retryable.deviceId(),
                                        notRetryable.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.summary.noChange").value(1))
                .andExpect(jsonPath("$.summary.success").value(1))
                .andExpect(jsonPath("$.summary.failed").value(2))
                .andReturn());

        String operationId = response.get("operationId").asText();
        mockMvc.perform(get("/api/operations/{id}", operationId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value(OperationType.SWITCH_MANAGED_ACCOUNT.name()))
                .andExpect(jsonPath("$.targets.length()").value(4));
        JsonNode retryables = read(mockMvc.perform(get("/api/operations/{id}/retryable-targets", operationId))
                        .andExpect(status().isOk())
                        .andReturn());
        assertThat(retryables).hasSize(1);
        assertThat(retryables.get(0).get("targetId").asText()).isEqualTo(retryable.deviceId());

        String payload = jdbcTemplate.queryForObject(
                "SELECT payload_json FROM batch_operations WHERE operation_id = ?",
                String.class,
                operationId);
        assertThat(objectMapper.readTree(payload).get("targetAccountId").asText()).isEqualTo("PRIMARY");
        assertThat(payload)
                .contains("\"schemaVersion\":1")
                .doesNotContain("password")
                .doesNotContain("credentialId")
                .doesNotContain("vault")
                .doesNotContain("SID")
                .doesNotContain("username")
                .doesNotContain("sessionId");
    }

    @Test
    void retryAuthorizationRunsBeforeOperationReadOrRemoteWork() throws Exception {
        long before = batchCount();
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));
        storageState.mark(MasterStorageStatus.UNAVAILABLE, ErrorCode.MASTER_DATABASE_UNAVAILABLE.name());

        mockMvc.perform(post("/api/operations/{id}/retry", "missing-operation")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));

        assertThat(batchCount()).isEqualTo(before);
        verifyNoInteractions(remoteOperationGateway);
    }

    @Test
    void retryRequestIsStrictAndDoesNotAcceptTargetAccountOverride() throws Exception {
        String classroomId = createClassroom("Aula retry validation " + id());
        String operationId = createStoredSwitchOperation(
                classroomId,
                ManagedWindowsAccountType.PRIMARY,
                List.of(operationResult("device-07", "PC07", TargetExecutionStatus.FAILED, ErrorCode.DEVICE_OFFLINE, 1)));

        expectInvalidRetry(operationId, null);
        expectInvalidRetry(operationId, Map.of("targetDeviceIds", List.of()));
        expectInvalidRetry(operationId, Map.of("targetDeviceIds", List.of(" ")));
        expectInvalidRetry(operationId, Map.of("targetDeviceIds", List.of("device-07", " device-07 ")));
        expectInvalidRetry(operationId, Map.of(
                "targetDeviceIds", List.of("device-07"),
                "targetAccountId", "SECONDARY"));

        for (String unsupported : List.of(
                "sourceAccountId",
                "password",
                "SID",
                "username",
                "credentialId",
                "vaultSessionToken",
                "force",
                "retryCount",
                "operationType",
                "newOperationId",
                "allFailed",
                "allRetryable",
                "classroomId",
                "command",
                "payload")) {
            expectInvalidRetry(operationId, Map.of(
                    "targetDeviceIds", List.of("device-07"),
                    unsupported, "not-accepted"));
        }
    }

    @Test
    void retryRejectsMissingOperationUnsupportedTypeAndIneligibleTargetsBeforeRemoteWork() throws Exception {
        mockMvc.perform(post("/api/operations/{id}/retry", "missing-operation")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value(ErrorCode.OPERATION_NOT_FOUND.name()));

        String classroomId = createClassroom("Aula retry eligibility " + id());
        String openUrlOperationId = id();
        batchOperationService.create(
                classroomId,
                BatchOperation.fromTargets(
                        openUrlOperationId,
                        OperationType.OPEN_URL,
                        "LOCAL_MASTER",
                        OffsetDateTime.parse("2026-09-06T12:00:00Z"),
                        List.of(operationResult(
                                "device-open-url",
                                "PC OPEN",
                                TargetExecutionStatus.FAILED,
                                ErrorCode.DEVICE_OFFLINE,
                                1))),
                OperationPayload.none());

        mockMvc.perform(post("/api/operations/{id}/retry", openUrlOperationId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of("device-open-url")))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.OPERATION_NOT_IMPLEMENTED.name()));

        String switchOperationId = createStoredSwitchOperation(
                classroomId,
                ManagedWindowsAccountType.PRIMARY,
                List.of(
                        operationResult("device-ok", "PC OK", TargetExecutionStatus.FAILED, ErrorCode.DEVICE_OFFLINE, 1),
                        operationResult("device-success", "PC SUCCESS", TargetExecutionStatus.SUCCESS, null, 1),
                        operationResult("device-no-change", "PC NO CHANGE", TargetExecutionStatus.NO_CHANGE, null, 1),
                        operationResult("device-pending", "PC PENDING", TargetExecutionStatus.PENDING, null, 1),
                        operationResult(
                                "device-non-retryable",
                                "PC NON RETRYABLE",
                                TargetExecutionStatus.FAILED,
                                ErrorCode.MANAGED_CREDENTIAL_NOT_CONFIGURED,
                                1),
                        operationResult(
                                "device-unknown",
                                "PC UNKNOWN",
                                TargetExecutionStatus.FAILED,
                                ErrorCode.OPERATION_RESULT_UNKNOWN,
                                1)));
        resetEndpointMocks();

        for (String ineligible : List.of(
                "device-success",
                "device-no-change",
                "device-pending",
                "device-non-retryable",
                "device-unknown",
                "not-in-batch")) {
            expectInvalidRetry(switchOperationId, Map.of("targetDeviceIds", List.of(ineligible)));
        }
        expectInvalidRetry(switchOperationId, Map.of("targetDeviceIds", List.of("device-ok", "device-success")));

        verifyNoInteractions(remoteOperationGateway);
        BatchTargetResult eligible = batchOperationRepository.findById(switchOperationId)
                .orElseThrow()
                .targets()
                .stream()
                .filter(result -> result.target().targetId().equals("device-ok"))
                .findFirst()
                .orElseThrow();
        assertThat(eligible.status()).isEqualTo(TargetExecutionStatus.FAILED);
        assertThat(eligible.attempt()).isEqualTo(1);
    }

    @Test
    void retryClaimsBeforeFreshSnapshotAndNoChangeUsesSameBatchOperation() throws Exception {
        String classroomId = createClassroom("Aula retry no change " + id());
        RegisteredClient target = registerClient(classroomId, "PC24", switchCapabilities());
        String operationId = createStoredSwitchOperation(
                classroomId,
                ManagedWindowsAccountType.PRIMARY,
                List.of(operationResult(
                        target.deviceId(),
                        "PC24",
                        TargetExecutionStatus.FAILED,
                        ErrorCode.DEVICE_OFFLINE,
                        1)));
        String originalPayload = payloadJson(operationId);
        long before = batchCount();
        resetEndpointMocks();
        when(remoteOperationGateway.getWindowsSessionState(any(), anyString(), eq(target.deviceId())))
                .thenAnswer(invocation -> {
                    BatchTargetResult claimed = batchOperationRepository.findById(operationId)
                            .orElseThrow()
                            .targets()
                            .getFirst();
                    assertThat(claimed.status()).isEqualTo(TargetExecutionStatus.PENDING);
                    assertThat(claimed.attempt()).isEqualTo(2);
                    return snapshotHandle(
                            invocation.getArgument(1),
                            invocation.getArgument(2),
                            com.galtek.classroom.network.v1.WindowsSessionState
                                    .WINDOWS_SESSION_STATE_PRIMARY_ACTIVE);
                });

        JsonNode response = read(mockMvc.perform(post("/api/operations/{id}/retry", operationId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(target.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.operationId").value(operationId))
                .andExpect(jsonPath("$.status").value("SUCCESS"))
                .andExpect(jsonPath("$.summary.noChange").value(1))
                .andExpect(jsonPath("$.targets[0].status").value(TargetExecutionStatus.NO_CHANGE.name()))
                .andExpect(jsonPath("$.targets[0].attempt").value(2))
                .andReturn());

        assertThat(response.get("operationId").asText()).isEqualTo(operationId);
        assertThat(batchCount()).isEqualTo(before);
        assertThat(payloadJson(operationId)).isEqualTo(originalPayload);
        verify(remoteOperationGateway, never()).switchManagedAccount(
                any(),
                anyString(),
                anyString(),
                any(ManagedWindowsAccountId.class));
    }

    @Test
    void retryUsesFreshRemoteOperationIdsAndTargetAccountFromOriginalPayload() throws Exception {
        String classroomId = createClassroom("Aula retry switch " + id());
        RegisteredClient target = registerClient(classroomId, "PC25", switchCapabilities());
        String operationId = createStoredSwitchOperation(
                classroomId,
                ManagedWindowsAccountType.SECONDARY,
                List.of(operationResult(
                        target.deviceId(),
                        "PC25",
                        TargetExecutionStatus.FAILED,
                        ErrorCode.WINDOWS_LOGON_FAILED,
                        1)));
        resetEndpointMocks();
        whenSnapshot(target, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_PRIMARY_ACTIVE);
        whenSwitchSuccess(target);

        mockMvc.perform(post("/api/operations/{id}/retry", operationId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(target.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.targetAccountId").value("SECONDARY"))
                .andExpect(jsonPath("$.targets[0].status").value(TargetExecutionStatus.SUCCESS.name()))
                .andExpect(jsonPath("$.targets[0].attempt").value(2));

        ArgumentCaptor<String> snapshotOperationId = ArgumentCaptor.forClass(String.class);
        ArgumentCaptor<String> switchOperationId = ArgumentCaptor.forClass(String.class);
        verify(remoteOperationGateway).getWindowsSessionState(any(), snapshotOperationId.capture(), eq(target.deviceId()));
        verify(remoteOperationGateway).switchManagedAccount(
                any(),
                switchOperationId.capture(),
                eq(target.deviceId()),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY));
        assertThat(snapshotOperationId.getValue()).isNotEqualTo(operationId);
        assertThat(switchOperationId.getValue()).isNotEqualTo(operationId);
        assertThat(snapshotOperationId.getValue()).isNotEqualTo(switchOperationId.getValue());
    }

    @Test
    void retryableTargetsReflectFinalRetryStateAndUnknownIsNeverRetryableForSwitch() throws Exception {
        String classroomId = createClassroom("Aula retry final state " + id());
        RegisteredClient target = registerClient(classroomId, "PC26", switchCapabilities());
        String operationId = createStoredSwitchOperation(
                classroomId,
                ManagedWindowsAccountType.PRIMARY,
                List.of(operationResult(
                        target.deviceId(),
                        "PC26",
                        TargetExecutionStatus.FAILED,
                        ErrorCode.DEVICE_OFFLINE,
                        1)));
        resetEndpointMocks();
        whenSnapshot(target, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_NO_SESSION);
        whenSwitchFailure(target, ErrorCode.WINDOWS_LOGON_FAILED);

        mockMvc.perform(post("/api/operations/{id}/retry", operationId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(target.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.targets[0].status").value(TargetExecutionStatus.FAILED.name()))
                .andExpect(jsonPath("$.targets[0].errorCode").value(ErrorCode.WINDOWS_LOGON_FAILED.name()))
                .andExpect(jsonPath("$.targets[0].retryable").value(true))
                .andExpect(jsonPath("$.targets[0].attempt").value(2));
        mockMvc.perform(get("/api/operations/{id}/retryable-targets", operationId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[0].targetId").value(target.deviceId()));

        resetEndpointMocks();
        whenSnapshot(target, com.galtek.classroom.network.v1.WindowsSessionState
                .WINDOWS_SESSION_STATE_NO_SESSION);
        whenSwitchFailure(target, ErrorCode.OPERATION_RESULT_UNKNOWN);

        mockMvc.perform(post("/api/operations/{id}/retry", operationId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(target.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.targets[0].status").value(TargetExecutionStatus.FAILED.name()))
                .andExpect(jsonPath("$.targets[0].errorCode").value(ErrorCode.OPERATION_RESULT_UNKNOWN.name()))
                .andExpect(jsonPath("$.targets[0].retryable").value(false))
                .andExpect(jsonPath("$.targets[0].attempt").value(3));
        mockMvc.perform(get("/api/operations/{id}/retryable-targets", operationId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$").isEmpty());
    }

    private void resetEndpointMocks() {
        reset(localAgentClient, remoteOperationGateway);
        storageState.markReady();
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria"));
        when(remoteOperationGateway.resultTimeout()).thenReturn(Duration.ofMillis(50));
        when(remoteOperationGateway.switchManagedAccountResultTimeout()).thenReturn(Duration.ofMillis(50));
    }

    private String createStoredSwitchOperation(
            String classroomId,
            ManagedWindowsAccountType targetAccountType,
            List<BatchTargetResult> targets) throws Exception {
        String operationId = id();
        batchOperationService.create(
                classroomId,
                BatchOperation.fromTargets(
                        operationId,
                        OperationType.SWITCH_MANAGED_ACCOUNT,
                        "LOCAL_MASTER",
                        OffsetDateTime.parse("2026-09-06T12:00:00Z"),
                        targets),
                new OperationPayload(1, objectMapper.writeValueAsString(Map.of(
                        "schemaVersion", 1,
                        "targetAccountId", targetAccountType.name()))));
        return operationId;
    }

    private BatchTargetResult operationResult(
            String deviceId,
            String displayName,
            TargetExecutionStatus status,
            ErrorCode errorCode,
            int attempt) {
        return new BatchTargetResult(
                new OperationTarget(OperationTargetType.DEVICE, deviceId, displayName),
                status,
                errorCode,
                errorCode == null ? null : "Stored " + errorCode.name() + ".",
                attempt);
    }

    private String payloadJson(String operationId) {
        return jdbcTemplate.queryForObject(
                "SELECT payload_json FROM batch_operations WHERE operation_id = ?",
                String.class,
                operationId);
    }

    private void expectInvalidRequest(String classroomId, Map<String, Object> body) throws Exception {
        var builder = post("/api/classrooms/{id}/managed-accounts/switch", classroomId)
                .contentType(MediaType.APPLICATION_JSON);
        if (body != null) {
            builder.content(json(body));
        }
        mockMvc.perform(builder)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));
    }

    private void expectInvalidRetry(String operationId, Map<String, Object> body) throws Exception {
        var builder = post("/api/operations/{id}/retry", operationId)
                .contentType(MediaType.APPLICATION_JSON);
        if (body != null) {
            builder.content(json(body));
        }
        mockMvc.perform(builder)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));
    }

    private void whenSnapshot(
            RegisteredClient client,
            com.galtek.classroom.network.v1.WindowsSessionState sessionState) {
        when(remoteOperationGateway.getWindowsSessionState(any(), anyString(), eq(client.deviceId())))
                .thenAnswer(invocation -> snapshotHandle(
                        invocation.getArgument(1),
                        invocation.getArgument(2),
                        sessionState));
    }

    private void whenSwitchSuccess(RegisteredClient client) {
        when(remoteOperationGateway.switchManagedAccount(
                any(),
                anyString(),
                eq(client.deviceId()),
                any(ManagedWindowsAccountId.class))).thenAnswer(invocation -> successHandle(
                        invocation.getArgument(1),
                        invocation.getArgument(2)));
    }

    private void whenSwitchFailure(RegisteredClient client, ErrorCode errorCode) {
        when(remoteOperationGateway.switchManagedAccount(
                any(),
                anyString(),
                eq(client.deviceId()),
                any(ManagedWindowsAccountId.class))).thenAnswer(invocation -> failedHandle(
                        invocation.getArgument(1),
                        invocation.getArgument(2),
                        errorCode,
                        messageFor(errorCode)));
    }

    private java.util.Optional<DispatchHandle> snapshotHandle(
            String operationId,
            String deviceId,
            com.galtek.classroom.network.v1.WindowsSessionState sessionState) {
        return java.util.Optional.of(new DispatchHandle(
                new RemoteOperationKey(deviceId, operationId),
                CompletableFuture.completedFuture(
                        RemoteOperationOutcome.success("Agent reported Windows session state.", sessionState))));
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
                CompletableFuture.completedFuture(RemoteOperationOutcome.failed(errorCode, message))));
    }

    private String messageFor(ErrorCode errorCode) {
        return "Agent reported " + errorCode.name() + ".";
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
                OffsetDateTime.parse("2026-09-06T12:00:00Z"),
                java.util.Set.of(),
                null), OffsetDateTime.parse("2026-09-06T12:00:00Z"));
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

    private List<NetworkCapability> switchCapabilities() {
        return List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_SESSION_AGENT_AVAILABLE,
                NetworkCapability.NETWORK_CAPABILITY_WINDOWS_SESSION_STATE_V1,
                NetworkCapability.NETWORK_CAPABILITY_WINDOWS_SESSION_SWITCH_V1);
    }

    private List<NetworkCapability> switchCapabilitiesWithoutSessionAgent() {
        return List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_WINDOWS_SESSION_STATE_V1,
                NetworkCapability.NETWORK_CAPABILITY_WINDOWS_SESSION_SWITCH_V1);
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
