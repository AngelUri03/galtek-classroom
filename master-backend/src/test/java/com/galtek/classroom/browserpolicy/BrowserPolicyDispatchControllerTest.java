package com.galtek.classroom.browserpolicy;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
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
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientNetworkIdentityDescriptor;
import com.galtek.classroom.network.MasterNetworkTransportConstants;
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
import com.galtek.classroom.network.v1.ApplyBrowserDownloadPolicyOperationParameters;
import com.galtek.classroom.network.v1.ApplyBrowserPolicyOperationParameters;
import com.galtek.classroom.network.v1.BrowserDownloadRestrictionMode;
import com.galtek.classroom.network.v1.BrowserPolicyAccountScope;
import com.galtek.classroom.network.v1.BrowserPolicyMode;
import com.galtek.classroom.network.v1.BrowserPolicyRuleMatchType;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.NetworkCapability;
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
class BrowserPolicyDispatchControllerTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-09-02T12:00:00Z");
    private static final Path DATA_DIR = Path.of(
            "target",
            "test-data",
            "browser-policy-dispatch-api-" + UUID.randomUUID());

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
                eq(OperationType.APPLY_BROWSER_NAVIGATION_POLICY),
                anyString(),
                anyString(),
                any(ApplyBrowserPolicyOperationParameters.class))).thenAnswer(invocation -> successHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3)));
        when(remoteOperationGateway.dispatch(
                any(),
                eq(OperationType.APPLY_BROWSER_DOWNLOAD_POLICY),
                anyString(),
                anyString(),
                any(ApplyBrowserDownloadPolicyOperationParameters.class))).thenAnswer(invocation -> successHandle(
                        invocation.getArgument(2),
                        invocation.getArgument(3)));
    }

    @Test
    void applyNavigationResolvesAnyPoliciesByDeviceContextAndBlocksEffectiveExactUrlOnly() throws Exception {
        String classroomId = createClassroom("Aula nav dispatch " + id());
        String groupId = createGroup(classroomId, "2", "A");
        String studentId = createStudent(classroomId, groupId, "Ana");
        RegisteredClient assigned = registerClient(classroomId, "PC01", navigationCapabilities());
        RegisteredClient noAssignment = registerClient(classroomId, "PC02", navigationCapabilities());
        RegisteredClient exactUrl = registerClient(classroomId, "PC03", navigationCapabilities());
        assign(studentId, assigned.deviceId());

        createNavigationPolicy(classroomId, Map.of(
                "name", "Classroom primary ignored in 16F1",
                "mode", "ALLOWLIST",
                "scopeType", "CLASSROOM",
                "accountScope", "PRIMARY"));
        JsonNode classroomAny = createNavigationPolicy(classroomId, Map.of(
                "name", "Classroom any",
                "mode", "ALLOWLIST",
                "scopeType", "CLASSROOM",
                "accountScope", "ANY"));
        createNavigationRule(classroomAny.get("policyId").asText(), Map.of(
                "action", "ALLOW",
                "matchType", "HOST_SUFFIX",
                "pattern", "school.edu"));
        JsonNode groupAny = createNavigationPolicy(classroomId, Map.of(
                "name", "Group any",
                "mode", "BLOCKLIST",
                "scopeType", "GROUP",
                "schoolGroupId", groupId,
                "accountScope", "ANY"));
        createNavigationRule(groupAny.get("policyId").asText(), Map.of(
                "action", "BLOCK",
                "matchType", "HOST_EXACT",
                "pattern", "games.test"));
        JsonNode exactPolicy = createNavigationPolicy(classroomId, Map.of(
                "name", "Exact native gap",
                "mode", "BLOCKLIST",
                "scopeType", "DEVICE",
                "deviceId", exactUrl.deviceId(),
                "accountScope", "ANY"));
        createNavigationRule(exactPolicy.get("policyId").asText(), Map.of(
                "action", "BLOCK",
                "matchType", "EXACT_URL",
                "pattern", "https://example.edu/video?id=1"));

        JsonNode response = read(mockMvc.perform(post("/api/classrooms/{id}/browser-policies/apply", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(
                                assigned.deviceId(),
                                noAssignment.deviceId(),
                                exactUrl.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value("APPLY_BROWSER_NAVIGATION_POLICY"))
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(2))
                .andExpect(jsonPath("$.failedCount").value(1))
                .andReturn());

        assertThat(target(response, exactUrl.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE.name());

        ArgumentCaptor<ApplyBrowserPolicyOperationParameters> params =
                ArgumentCaptor.forClass(ApplyBrowserPolicyOperationParameters.class);
        ArgumentCaptor<String> operationIds = ArgumentCaptor.forClass(String.class);
        ArgumentCaptor<String> targetIds = ArgumentCaptor.forClass(String.class);
        verify(remoteOperationGateway, org.mockito.Mockito.times(2)).dispatch(
                any(),
                eq(OperationType.APPLY_BROWSER_NAVIGATION_POLICY),
                operationIds.capture(),
                targetIds.capture(),
                params.capture());

        String operationId = response.get("operationId").asText();
        assertThat(operationIds.getAllValues()).containsExactly(operationId, operationId);
        assertThat(targetIds.getAllValues()).containsExactly(assigned.deviceId(), noAssignment.deviceId());

        ApplyBrowserPolicyOperationParameters assignedParams = params.getAllValues().get(0);
        assertThat(assignedParams.getPolicyId()).isEqualTo(groupAny.get("policyId").asText());
        assertThat(assignedParams.getMode()).isEqualTo(BrowserPolicyMode.BROWSER_POLICY_MODE_BLOCKLIST);
        assertThat(assignedParams.getAccountScope()).isEqualTo(BrowserPolicyAccountScope.BROWSER_POLICY_ACCOUNT_SCOPE_ANY);
        assertThat(assignedParams.getRules(0).getMatchType())
                .isEqualTo(BrowserPolicyRuleMatchType.BROWSER_POLICY_RULE_MATCH_TYPE_HOST_EXACT);
        assertThat(assignedParams.getRules(0).getPattern()).isEqualTo("games.test");

        ApplyBrowserPolicyOperationParameters unassignedParams = params.getAllValues().get(1);
        assertThat(unassignedParams.getPolicyId()).isEqualTo(classroomAny.get("policyId").asText());
        assertThat(unassignedParams.getMode()).isEqualTo(BrowserPolicyMode.BROWSER_POLICY_MODE_ALLOWLIST);
        assertThat(unassignedParams.getRules(0).getPattern()).isEqualTo("school.edu");

        OperationResponse persisted = adminRepository.findOperation(operationId).orElseThrow();
        assertThat(persisted.type()).isEqualTo(OperationType.APPLY_BROWSER_NAVIGATION_POLICY.name());
        assertThat(persisted.targetCount()).isEqualTo(3);
    }

    @Test
    void applyNavigationDoesNotBlockWhenExactUrlPolicyIsNotEffective() throws Exception {
        String classroomId = createClassroom("Aula nav exact lower " + id());
        RegisteredClient pc = registerClient(classroomId, "PC04", navigationCapabilities());
        JsonNode classroomPolicy = createNavigationPolicy(classroomId, Map.of(
                "name", "Classroom exact",
                "mode", "BLOCKLIST",
                "scopeType", "CLASSROOM",
                "accountScope", "ANY"));
        createNavigationRule(classroomPolicy.get("policyId").asText(), Map.of(
                "action", "BLOCK",
                "matchType", "EXACT_URL",
                "pattern", "https://example.edu/blocked?id=1"));
        createNavigationPolicy(classroomId, Map.of(
                "name", "Device unrestricted wins",
                "mode", "UNRESTRICTED",
                "scopeType", "DEVICE",
                "deviceId", pc.deviceId(),
                "accountScope", "ANY"));

        mockMvc.perform(post("/api/classrooms/{id}/browser-policies/apply", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(pc.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("SUCCESS"));

        verify(remoteOperationGateway).dispatch(
                any(),
                eq(OperationType.APPLY_BROWSER_NAVIGATION_POLICY),
                anyString(),
                eq(pc.deviceId()),
                any(ApplyBrowserPolicyOperationParameters.class));
    }

    @Test
    void applyDownloadFreezesExplicitAndImplicitPolicyParameters() throws Exception {
        String classroomId = createClassroom("Aula download dispatch " + id());
        String groupId = createGroup(classroomId, "3", "B");
        String studentId = createStudent(classroomId, groupId, "Luis");
        RegisteredClient explicitNoSpecial = registerClient(classroomId, "PC05", downloadCapabilities());
        RegisteredClient groupTarget = registerClient(classroomId, "PC06", downloadCapabilities());
        RegisteredClient implicit = registerClient(classroomId, "PC07", downloadCapabilities());
        assign(studentId, groupTarget.deviceId());

        createDownloadPolicy(classroomId, Map.of(
                "name", "Classroom primary ignored",
                "restrictionMode", "BLOCK_ALL",
                "scopeType", "CLASSROOM",
                "accountScope", "PRIMARY"));
        JsonNode groupPolicy = createDownloadPolicy(classroomId, Map.of(
                "name", "Group block all",
                "restrictionMode", "BLOCK_ALL",
                "scopeType", "GROUP",
                "schoolGroupId", groupId,
                "accountScope", "ANY"));
        JsonNode explicitZero = createDownloadPolicy(classroomId, Map.of(
                "name", "Device explicit zero",
                "restrictionMode", "NO_SPECIAL_RESTRICTIONS",
                "scopeType", "DEVICE",
                "deviceId", explicitNoSpecial.deviceId(),
                "accountScope", "ANY"));

        JsonNode response = read(mockMvc.perform(post(
                        "/api/classrooms/{id}/browser-download-policies/apply",
                        classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(
                                explicitNoSpecial.deviceId(),
                                groupTarget.deviceId(),
                                implicit.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value("APPLY_BROWSER_DOWNLOAD_POLICY"))
                .andExpect(jsonPath("$.status").value("SUCCESS"))
                .andReturn());

        ArgumentCaptor<ApplyBrowserDownloadPolicyOperationParameters> params =
                ArgumentCaptor.forClass(ApplyBrowserDownloadPolicyOperationParameters.class);
        verify(remoteOperationGateway, org.mockito.Mockito.times(3)).dispatch(
                any(),
                eq(OperationType.APPLY_BROWSER_DOWNLOAD_POLICY),
                eq(response.get("operationId").asText()),
                anyString(),
                params.capture());

        ApplyBrowserDownloadPolicyOperationParameters explicitParams = params.getAllValues().get(0);
        assertThat(explicitParams.getPolicyId()).isEqualTo(explicitZero.get("policyId").asText());
        assertThat(explicitParams.getImplicitNoSpecialRestrictions()).isFalse();
        assertThat(explicitParams.getRestrictionMode())
                .isEqualTo(BrowserDownloadRestrictionMode
                        .BROWSER_DOWNLOAD_RESTRICTION_MODE_NO_SPECIAL_RESTRICTIONS);

        ApplyBrowserDownloadPolicyOperationParameters groupParams = params.getAllValues().get(1);
        assertThat(groupParams.getPolicyId()).isEqualTo(groupPolicy.get("policyId").asText());
        assertThat(groupParams.getRestrictionMode())
                .isEqualTo(BrowserDownloadRestrictionMode.BROWSER_DOWNLOAD_RESTRICTION_MODE_BLOCK_ALL);

        ApplyBrowserDownloadPolicyOperationParameters implicitParams = params.getAllValues().get(2);
        assertThat(implicitParams.getPolicyId()).isEmpty();
        assertThat(implicitParams.getPolicyVersion()).isZero();
        assertThat(implicitParams.getImplicitNoSpecialRestrictions()).isTrue();
        assertThat(implicitParams.getRestrictionMode())
                .isEqualTo(BrowserDownloadRestrictionMode
                        .BROWSER_DOWNLOAD_RESTRICTION_MODE_NO_SPECIAL_RESTRICTIONS);
        assertThat(implicitParams.getAccountScope())
                .isEqualTo(BrowserPolicyAccountScope.BROWSER_POLICY_ACCOUNT_SCOPE_ANY);
    }

    @Test
    void browserApplyPreflightFailuresDoNotCancelReadyTargets() throws Exception {
        String classroomId = createClassroom("Aula preflight " + id());
        RegisteredClient ready = registerClient(classroomId, "PC08", downloadCapabilities());
        RegisteredClient noCapability = registerClient(classroomId, "PC09", List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1));
        RegisteredClient offline = registerClient(classroomId, "PC10", downloadCapabilities());
        connectionRegistry.markOffline(
                offline.client().descriptor().clientNetworkIdentityId(),
                connectionId(offline.client()),
                "TEST_OFFLINE");
        String otherClassroom = createClassroom("Aula otra " + id());
        RegisteredClient other = registerClient(otherClassroom, "PC11", downloadCapabilities());

        JsonNode response = read(mockMvc.perform(post(
                        "/api/classrooms/{id}/browser-download-policies/apply",
                        classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of(
                                ready.deviceId(),
                                noCapability.deviceId(),
                                offline.deviceId(),
                                other.deviceId())))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("PARTIAL_SUCCESS"))
                .andExpect(jsonPath("$.successCount").value(1))
                .andExpect(jsonPath("$.failedCount").value(3))
                .andReturn());

        assertThat(target(response, noCapability.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.CAPABILITY_NOT_SUPPORTED.name());
        assertThat(target(response, offline.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_OFFLINE.name());
        assertThat(target(response, other.deviceId()).get("errorCode").asText())
                .isEqualTo(ErrorCode.DEVICE_NOT_FOUND.name());
        verify(remoteOperationGateway).dispatch(
                any(),
                eq(OperationType.APPLY_BROWSER_DOWNLOAD_POLICY),
                eq(response.get("operationId").asText()),
                eq(ready.deviceId()),
                any(ApplyBrowserDownloadPolicyOperationParameters.class));
    }

    @Test
    void browserApplyRequiresAuthorizationBeforeStorageAndRejectsFabricatedPolicyFields() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));
        storageState.mark(MasterStorageStatus.UNAVAILABLE, ErrorCode.MASTER_DATABASE_UNAVAILABLE.name());

        mockMvc.perform(post("/api/classrooms/{id}/browser-policies/apply", "missing")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("targetDeviceIds", List.of("device-1")))))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));
        verifyNoInteractions(remoteOperationGateway);

        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria"));
        storageState.markReady();

        mockMvc.perform(post("/api/classrooms/{id}/browser-policies/apply", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetDeviceIds", List.of("device-1"),
                                "policyId", "forged"))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));

        mockMvc.perform(post("/api/classrooms/{id}/browser-download-policies/apply", "classroom-1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "targetDeviceIds", List.of("device-1"),
                                "accountType", "PRIMARY"))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(ErrorCode.INVALID_REQUEST.name()));
    }

    private java.util.Optional<DispatchHandle> successHandle(String operationId, String deviceId) {
        return java.util.Optional.of(new DispatchHandle(
                new RemoteOperationKey(deviceId, operationId),
                CompletableFuture.completedFuture(
                        RemoteOperationOutcome.success("Agent reported operation success."))));
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

    private JsonNode createDownloadPolicy(String classroomId, Map<String, Object> body) throws Exception {
        return read(mockMvc.perform(post("/api/classrooms/{id}/browser-download-policies", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(body)))
                .andExpect(status().isCreated())
                .andReturn());
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

    private List<NetworkCapability> navigationCapabilities() {
        return List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_BROWSER_NAVIGATION_POLICY_V1);
    }

    private List<NetworkCapability> downloadCapabilities() {
        return List.of(
                NetworkCapability.NETWORK_CAPABILITY_HEARTBEAT_V1,
                NetworkCapability.NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1,
                NetworkCapability.NETWORK_CAPABILITY_BROWSER_DOWNLOAD_POLICY_V1);
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
