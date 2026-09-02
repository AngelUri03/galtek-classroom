package com.galtek.classroom.browserpolicy;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.reset;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.patch;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceManagementService;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.nio.file.Path;
import java.time.OffsetDateTime;
import java.util.EnumSet;
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
class BrowserPolicyControllerTest {

    private static final Path DATA_DIR = Path.of(
            "target",
            "test-data",
            "browser-policy-api-" + UUID.randomUUID());

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
    private DeviceManagementService deviceManagementService;

    @Autowired
    private MasterStorageState storageState;

    @MockitoBean
    private LocalAgentClient localAgentClient;

    @BeforeEach
    void authorizeMaster() {
        reset(localAgentClient);
        storageState.markReady();
        when(localAgentClient.getMasterAuthorization()).thenReturn(authorized());
    }

    @Test
    void crudArchivePolicyAndRule() throws Exception {
        String classroomId = createClassroom("Aula policy " + id());

        JsonNode policy = createPolicy(classroomId, Map.of(
                "name", "Videos clase",
                "mode", "BLOCKLIST",
                "scopeType", "CLASSROOM",
                "accountScope", "PRIMARY"));

        JsonNode updatedPolicy = read(mockMvc.perform(patch("/api/browser-policies/{id}", policy.get("policyId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "name", "Videos clase primaria",
                                "mode", "ALLOWLIST",
                                "expectedVersion", policy.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.mode").value("ALLOWLIST"))
                .andExpect(jsonPath("$.version").value(1))
                .andReturn());

        JsonNode rule = read(mockMvc.perform(post("/api/browser-policies/{id}/rules", policy.get("policyId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "action", "ALLOW",
                                "matchType", "EXACT_URL",
                                "pattern", "https://www.youtube.com/watch?v=ABC123#ignored",
                                "description", "Video exacto"))))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.pattern").value("https://www.youtube.com/watch?v=ABC123"))
                .andReturn());

        JsonNode updatedRule = read(mockMvc.perform(patch("/api/browser-url-rules/{id}", rule.get("ruleId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "action", "BLOCK",
                                "matchType", "HOST_SUFFIX",
                                "pattern", "YouTube.COM.",
                                "enabled", true,
                                "expectedVersion", rule.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.pattern").value("youtube.com"))
                .andReturn());

        mockMvc.perform(post("/api/browser-url-rules/{id}/archive", rule.get("ruleId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("expectedVersion", updatedRule.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.enabled").value(false));

        mockMvc.perform(post("/api/browser-policies/{id}/archive", policy.get("policyId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("expectedVersion", updatedPolicy.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.active").value(false));
    }

    @Test
    void duplicateActivePolicyConflictsButPrimaryAndSecondaryCoexist() throws Exception {
        String classroomId = createClassroom("Aula unique " + id());
        createPolicy(classroomId, Map.of(
                "name", "Primary",
                "mode", "ALLOWLIST",
                "scopeType", "CLASSROOM",
                "accountScope", "PRIMARY"));
        createPolicy(classroomId, Map.of(
                "name", "Secondary",
                "mode", "UNRESTRICTED",
                "scopeType", "CLASSROOM",
                "accountScope", "SECONDARY"));

        mockMvc.perform(post("/api/classrooms/{id}/browser-policies", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "name", "Duplicate primary",
                                "mode", "BLOCKLIST",
                                "scopeType", "CLASSROOM",
                                "accountScope", "PRIMARY"))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("BROWSER_POLICY_CONFLICT"));
    }

    @Test
    void invalidScopeReferencesAndPatternsAreRejected() throws Exception {
        String classroomA = createClassroom("Aula A " + id());
        String classroomB = createClassroom("Aula B " + id());
        String groupB = createGroup(classroomB, "1", "B");
        String deviceB = createDevice(classroomB, "PC99");

        mockMvc.perform(post("/api/classrooms/{id}/browser-policies", classroomA)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "name", "Wrong group",
                                "mode", "ALLOWLIST",
                                "scopeType", "GROUP",
                                "schoolGroupId", groupB,
                                "accountScope", "ANY"))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("BROWSER_POLICY_SCOPE_INVALID"));

        mockMvc.perform(post("/api/classrooms/{id}/browser-policies", classroomA)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "name", "Wrong device",
                                "mode", "ALLOWLIST",
                                "scopeType", "DEVICE",
                                "deviceId", deviceB,
                                "accountScope", "ANY"))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("BROWSER_POLICY_SCOPE_INVALID"));

        JsonNode policy = createPolicy(classroomA, Map.of(
                "name", "Valid",
                "mode", "ALLOWLIST",
                "scopeType", "CLASSROOM",
                "accountScope", "ANY"));

        mockMvc.perform(post("/api/browser-policies/{id}/rules", policy.get("policyId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "action", "ALLOW",
                                "matchType", "URL_PREFIX",
                                "pattern", "https://escuela.local/material/?unit=1"))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("BROWSER_POLICY_RULE_INVALID"));
    }

    @Test
    void effectiveEndpointAppliesPrecedence() throws Exception {
        String classroomId = createClassroom("Aula effective " + id());
        String groupId = createGroup(classroomId, "2", "A");
        String deviceId = createDevice(classroomId, "PC07");

        createPolicy(classroomId, Map.of(
                "name", "Classroom primary",
                "mode", "ALLOWLIST",
                "scopeType", "CLASSROOM",
                "accountScope", "PRIMARY"));
        createPolicy(classroomId, Map.of(
                "name", "Group any",
                "mode", "BLOCKLIST",
                "scopeType", "GROUP",
                "schoolGroupId", groupId,
                "accountScope", "ANY"));
        createPolicy(classroomId, Map.of(
                "name", "Device unrestricted",
                "mode", "UNRESTRICTED",
                "scopeType", "DEVICE",
                "deviceId", deviceId,
                "accountScope", "ANY"));

        mockMvc.perform(get("/api/classrooms/{id}/browser-policies/effective", classroomId)
                        .queryParam("deviceId", deviceId)
                        .queryParam("groupId", groupId)
                        .queryParam("accountType", "PRIMARY"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.implicit").value(false))
                .andExpect(jsonPath("$.mode").value("UNRESTRICTED"))
                .andExpect(jsonPath("$.policy.scopeType").value("DEVICE"));

        String emptyClassroom = createClassroom("Aula empty " + id());
        mockMvc.perform(get("/api/classrooms/{id}/browser-policies/effective", emptyClassroom))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.implicit").value(true))
                .andExpect(jsonPath("$.mode").value("UNRESTRICTED"));
    }

    @Test
    void guardRunsBeforeStorageCheckForAdministrativeEndpoints() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));
        storageState.mark(MasterStorageStatus.UNAVAILABLE, ErrorCode.MASTER_DATABASE_UNAVAILABLE.name());

        mockMvc.perform(get("/api/classrooms/missing/browser-policies"))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));
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

    private JsonNode createPolicy(String classroomId, Map<String, Object> body) throws Exception {
        return read(mockMvc.perform(post("/api/classrooms/{id}/browser-policies", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(body)))
                .andExpect(status().isCreated())
                .andReturn());
    }

    private String createDevice(String classroomId, String displayName) {
        String deviceId = id();
        deviceManagementService.register(
                classroomId,
                new Device(
                        deviceId,
                        id(),
                        displayName,
                        displayName.toLowerCase(),
                        DeviceStatus.ONLINE,
                        OffsetDateTime.parse("2026-09-01T10:00:00Z"),
                        EnumSet.of(DeviceCapability.LOCAL_IPC, DeviceCapability.SESSION_AGENT),
                        null));
        return deviceId;
    }

    private JsonNode read(org.springframework.test.web.servlet.MvcResult result) throws Exception {
        return objectMapper.readTree(result.getResponse().getContentAsString());
    }

    private String json(Object value) throws Exception {
        return objectMapper.writeValueAsString(value);
    }

    private MasterAuthorizationResponse authorized() {
        return new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria");
    }

    private String id() {
        return UUID.randomUUID().toString();
    }
}
