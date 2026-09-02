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
class BrowserDownloadPolicyControllerTest {

    private static final Path DATA_DIR = Path.of(
            "target",
            "test-data",
            "browser-download-policy-api-" + UUID.randomUUID());

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
    void guardRunsBeforeStorageCheckForAdministrativeEndpoints() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));
        storageState.mark(MasterStorageStatus.UNAVAILABLE, ErrorCode.MASTER_DATABASE_UNAVAILABLE.name());

        mockMvc.perform(get("/api/classrooms/missing/browser-download-policies"))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));
    }

    @Test
    void listCreatePatchArchiveAndEffectivePolicy() throws Exception {
        String classroomId = createClassroom("Aula downloads " + id());
        String groupId = createGroup(classroomId, "2", "A");
        String deviceId = createDevice(classroomId, "PC07");

        createPolicy(classroomId, Map.of(
                "name", "Classroom primary",
                "restrictionMode", "BLOCK_ALL",
                "scopeType", "CLASSROOM",
                "accountScope", "PRIMARY"));
        createPolicy(classroomId, Map.of(
                "name", "Group any",
                "restrictionMode", "BLOCK_DANGEROUS",
                "scopeType", "GROUP",
                "schoolGroupId", groupId,
                "accountScope", "ANY"));
        JsonNode devicePolicy = createPolicy(classroomId, Map.of(
                "name", "PC07 unrestricted",
                "restrictionMode", "NO_SPECIAL_RESTRICTIONS",
                "scopeType", "DEVICE",
                "deviceId", deviceId,
                "accountScope", "ANY"));

        mockMvc.perform(get("/api/classrooms/{id}/browser-download-policies", classroomId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.length()").value(3));

        JsonNode updatedPolicy = read(mockMvc.perform(patch(
                        "/api/browser-download-policies/{id}",
                        devicePolicy.get("policyId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "name", "PC07 bloquea todo",
                                "restrictionMode", "BLOCK_ALL",
                                "expectedVersion", devicePolicy.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.restrictionMode").value("BLOCK_ALL"))
                .andExpect(jsonPath("$.version").value(1))
                .andReturn());

        mockMvc.perform(get("/api/classrooms/{id}/browser-download-policies/effective", classroomId)
                        .queryParam("deviceId", deviceId)
                        .queryParam("groupId", groupId)
                        .queryParam("accountType", "PRIMARY"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.implicit").value(false))
                .andExpect(jsonPath("$.restrictionMode").value("BLOCK_ALL"))
                .andExpect(jsonPath("$.policy.scopeType").value("DEVICE"));

        mockMvc.perform(post(
                        "/api/browser-download-policies/{id}/archive",
                        devicePolicy.get("policyId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("expectedVersion", updatedPolicy.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.active").value(false));

        String emptyClassroom = createClassroom("Aula no downloads " + id());
        mockMvc.perform(get("/api/classrooms/{id}/browser-download-policies/effective", emptyClassroom))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.implicit").value(true))
                .andExpect(jsonPath("$.restrictionMode").value("NO_SPECIAL_RESTRICTIONS"));
    }

    @Test
    void duplicateActivePolicyConflictsButPrimaryAndSecondaryCoexist() throws Exception {
        String classroomId = createClassroom("Aula unique downloads " + id());
        createPolicy(classroomId, Map.of(
                "name", "Primary",
                "restrictionMode", "BLOCK_ALL",
                "scopeType", "CLASSROOM",
                "accountScope", "PRIMARY"));
        createPolicy(classroomId, Map.of(
                "name", "Secondary",
                "restrictionMode", "BLOCK_MALICIOUS",
                "scopeType", "CLASSROOM",
                "accountScope", "SECONDARY"));

        mockMvc.perform(post("/api/classrooms/{id}/browser-download-policies", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "name", "Duplicate primary",
                                "restrictionMode", "BLOCK_DANGEROUS",
                                "scopeType", "CLASSROOM",
                                "accountScope", "PRIMARY"))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("BROWSER_DOWNLOAD_POLICY_CONFLICT"));
    }

    @Test
    void invalidModeInvalidScopeNotFoundAndUnsupportedFieldsAreRejected() throws Exception {
        String classroomA = createClassroom("Aula A downloads " + id());
        String classroomB = createClassroom("Aula B downloads " + id());
        String groupB = createGroup(classroomB, "1", "B");
        String deviceB = createDevice(classroomB, "PC99");

        mockMvc.perform(post("/api/classrooms/{id}/browser-download-policies", classroomA)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "name", "Bad mode",
                                "restrictionMode", "BLOCK_ZIP",
                                "scopeType", "CLASSROOM",
                                "accountScope", "ANY"))))
                .andExpect(status().isBadRequest());

        mockMvc.perform(post("/api/classrooms/{id}/browser-download-policies", classroomA)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "name", "Wrong group",
                                "restrictionMode", "BLOCK_ALL",
                                "scopeType", "GROUP",
                                "schoolGroupId", groupB,
                                "accountScope", "ANY"))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID"));

        mockMvc.perform(get("/api/classrooms/{id}/browser-download-policies/effective", classroomA)
                        .queryParam("deviceId", deviceB))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID"));

        mockMvc.perform(patch("/api/browser-download-policies/{id}", id())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "restrictionMode", "BLOCK_ALL",
                                "expectedVersion", 0))))
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("BROWSER_DOWNLOAD_POLICY_NOT_FOUND"));

        mockMvc.perform(post("/api/classrooms/{id}/browser-download-policies", classroomA)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("""
                                {
                                  "name": "Unsupported",
                                  "restrictionMode": "BLOCK_ALL",
                                  "scopeType": "CLASSROOM",
                                  "accountScope": "ANY",
                                  "blockedExtensions": [".exe"]
                                }
                                """))
                .andExpect(status().isBadRequest());
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
        return read(mockMvc.perform(post("/api/classrooms/{id}/browser-download-policies", classroomId)
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
