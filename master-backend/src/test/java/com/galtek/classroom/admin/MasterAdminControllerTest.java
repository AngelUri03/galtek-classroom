package com.galtek.classroom.admin;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.reset;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.patch;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.galtek.classroom.application.ApplicationAvailability;
import com.galtek.classroom.application.ApplicationCatalogService;
import com.galtek.classroom.application.ApplicationDefinition;
import com.galtek.classroom.application.ApplicationType;
import com.galtek.classroom.application.LaunchPolicy;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceManagementService;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.DeviceStatusResponse;
import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.LocalAgentUnavailableException;
import com.galtek.classroom.localagent.MachineCodeResponse;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.operations.BatchOperation;
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
import java.time.OffsetDateTime;
import java.util.EnumSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
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
class MasterAdminControllerTest {

    private static final Path DATA_DIR = Path.of(
            "target",
            "test-data",
            "admin-api-" + UUID.randomUUID());

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
    private ApplicationCatalogService applicationCatalogService;

    @Autowired
    private BatchOperationService batchOperationService;

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
    void adminEndpointsRequireMasterAuthorizationAndMapAgentDown() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "CURRENT_ACCOUNT_NOT_AUTHORIZED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\Soporte"));

        mockMvc.perform(get("/api/classrooms"))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("CURRENT_ACCOUNT_NOT_AUTHORIZED"));

        when(localAgentClient.getMasterAuthorization()).thenThrow(new LocalAgentUnavailableException("down"));

        mockMvc.perform(get("/api/classrooms"))
                .andExpect(status().isServiceUnavailable())
                .andExpect(jsonPath("$.code").value("LOCAL_AGENT_UNAVAILABLE"));
    }

    @Test
    void diagnosticsEndpointsRemainPublicWithoutMasterGuard() throws Exception {
        when(localAgentClient.getMasterAuthorization()).thenReturn(new MasterAuthorizationResponse(
                "MASTER_LICENSE_REQUIRED",
                false,
                true,
                "AULA\\MaestraPrimaria",
                "AULA\\MaestraPrimaria"));
        when(localAgentClient.getDeviceStatus()).thenReturn(new DeviceStatusResponse(
                "GALTEK_CLASSROOM",
                id(),
                "PC-AULA-07",
                "ACTIVATION_REQUIRED",
                false,
                null,
                null,
                null,
                OffsetDateTime.parse("2026-08-26T10:00:00Z"),
                List.of(),
                Map.of()));
        when(localAgentClient.getMachineCode()).thenReturn(new MachineCodeResponse("machine-code-1"));

        mockMvc.perform(get("/api/system/health"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("UP"));
        mockMvc.perform(get("/api/device/status"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.product").value("GALTEK_CLASSROOM"));
        mockMvc.perform(get("/api/device/machine-code"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.machineCode").value("machine-code-1"));
        mockMvc.perform(get("/api/master/authorization"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.authorized").value(false));
    }

    @Test
    void classroomAndGroupCrudArchiveAndOptimisticConcurrency() throws Exception {
        String classroomId = createClassroom("Aula " + id());
        JsonNode patchedClassroom = read(mockMvc.perform(patch("/api/classrooms/{id}", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "displayName", "Aula Nueva",
                                "expectedVersion", 0))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.displayName").value("Aula Nueva"))
                .andExpect(jsonPath("$.version").value(1))
                .andReturn());

        mockMvc.perform(patch("/api/classrooms/{id}", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "displayName", "Aula Vieja",
                                "expectedVersion", 0))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("CONCURRENT_MODIFICATION"));

        String groupId = createGroup(classroomId, "2", "B");
        JsonNode group = read(mockMvc.perform(patch("/api/groups/{id}", groupId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "displayName", "2 B - Computo",
                                "expectedVersion", 0))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.displayName").value("2 B - Computo"))
                .andReturn());

        mockMvc.perform(post("/api/groups/{id}/archive", groupId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("expectedVersion", group.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.active").value(false));

        mockMvc.perform(post("/api/classrooms/{id}/archive", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("expectedVersion", patchedClassroom.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.active").value(false));
    }

    @Test
    void archiveRejectsClassroomAndGroupWithActiveContent() throws Exception {
        String classroomId = createClassroom("Aula con contenido " + id());
        String groupId = createGroup(classroomId, "1", "A");

        mockMvc.perform(post("/api/classrooms/{id}/archive", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("expectedVersion", 0))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("CLASSROOM_HAS_ACTIVE_CONTENT"));

        createStudent(classroomId, groupId, "Ana", "Lopez");

        mockMvc.perform(post("/api/groups/{id}/archive", groupId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("expectedVersion", 0))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("GROUP_HAS_ACTIVE_STUDENTS"));
    }

    @Test
    void studentIndividualBatchDuplicateNamesSearchFilterAndArchiveWork() throws Exception {
        String classroomId = createClassroom("Aula alumnos " + id());
        String groupA = createGroup(classroomId, "1", "A");
        String groupB = createGroup(classroomId, "1", "B");
        JsonNode juan = createStudent(classroomId, groupA, "Juan", "Perez");

        JsonNode patchedJuan = read(mockMvc.perform(patch("/api/students/{id}", juan.get("studentId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "displayName", "Juan P.",
                                "expectedVersion", juan.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.displayName").value("Juan P."))
                .andReturn());

        mockMvc.perform(post("/api/classrooms/{id}/students/batch", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("students", List.of(
                                Map.of(
                                        "clientReference", "row-1",
                                        "groupId", groupB,
                                        "firstName", "Alicia",
                                        "lastName", "Ramos"),
                                Map.of(
                                        "clientReference", "row-2",
                                        "groupId", groupB,
                                        "firstName", "Alicia",
                                        "lastName", "Ramos"))))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.successCount").value(2))
                .andExpect(jsonPath("$.failedCount").value(0));

        JsonNode partial = read(mockMvc.perform(post("/api/classrooms/{id}/students/batch", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("students", List.of(
                                Map.of(
                                        "clientReference", "ok",
                                        "groupId", groupA,
                                        "firstName", "Mario",
                                        "lastName", "Ruiz"),
                                Map.of(
                                        "clientReference", "bad",
                                        "groupId", groupA,
                                        "lastName", "SinNombre"))))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.successCount").value(1))
                .andExpect(jsonPath("$.failedCount").value(1))
                .andReturn());
        assertThat(partial.get("results").findValuesAsText("errorCode"))
                .contains(ErrorCode.INVALID_REQUEST.name());

        mockMvc.perform(get("/api/classrooms/{id}/students", classroomId)
                        .queryParam("groupId", groupB)
                        .queryParam("search", "Alicia"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.length()").value(2))
                .andExpect(jsonPath("$[0].displayName").value("Alicia Ramos"))
                .andExpect(jsonPath("$[1].displayName").value("Alicia Ramos"));

        mockMvc.perform(post("/api/students/{id}/archive", juan.get("studentId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("expectedVersion", patchedJuan.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.active").value(false));

        mockMvc.perform(get("/api/classrooms/{id}/students", classroomId)
                        .queryParam("search", "Juan"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.length()").value(0));
        mockMvc.perform(get("/api/classrooms/{id}/students", classroomId)
                        .queryParam("active", "false")
                        .queryParam("search", "Juan"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.length()").value(1));

        JsonNode toArchive = createStudent(classroomId, groupA, "Sofia", "Garcia");
        Map<String, Object> archiveBatchRequest = Map.of("students", List.of(
                Map.of(
                        "clientReference", "archive-ok",
                        "studentId", toArchive.get("studentId").asText(),
                        "expectedVersion", toArchive.get("version").asLong()),
                Map.of(
                        "clientReference", "archive-bad",
                        "studentId", "missing-student",
                        "expectedVersion", 0)));
        mockMvc.perform(post("/api/students/archive-batch")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(archiveBatchRequest)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.successCount").value(1))
                .andExpect(jsonPath("$.failedCount").value(1));
    }

    @Test
    void assignmentsBatchHistorySnapshotOperationsAndRetryablesWork() throws Exception {
        String classroomId = createClassroom("Aula assignments " + id());
        String groupId = createGroup(classroomId, "3", "A");
        JsonNode student1 = createStudent(classroomId, groupId, "Luis", "Nava");
        JsonNode student2 = createStudent(classroomId, groupId, "Mia", "Ortega");
        JsonNode student3 = createStudent(classroomId, groupId, "Eva", "Santos");
        JsonNode student4 = createStudent(classroomId, groupId, "Leo", "Cruz");
        JsonNode student5 = createStudent(classroomId, groupId, "Zoe", "Diaz");
        String pc01 = createDevice(classroomId, "PC01");
        String pc02 = createDevice(classroomId, "PC02");
        String pc03 = createDevice(classroomId, "PC03");
        String pc04 = createDevice(classroomId, "PC04");

        JsonNode firstAssignment = read(mockMvc.perform(post("/api/assignments")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "studentId", student1.get("studentId").asText(),
                                "deviceId", pc01))))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.current").value(true))
                .andReturn());

        mockMvc.perform(post("/api/assignments")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "studentId", student2.get("studentId").asText(),
                                "deviceId", pc01))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("TARGET_OCCUPIED"));

        mockMvc.perform(post("/api/assignments/{id}/close", firstAssignment.get("assignmentId").asText())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of("expectedVersion", firstAssignment.get("version").asLong()))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.current").value(false))
                .andExpect(jsonPath("$.status").value("ENDED"));

        mockMvc.perform(post("/api/assignments")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "studentId", student1.get("studentId").asText(),
                                "deviceId", pc02))))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.deviceId").value(pc02));

        mockMvc.perform(get("/api/classrooms/{id}/assignments", classroomId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.length()").value(2));

        Map<String, Object> assignmentBatchRequest = Map.of(
                "classroomId", classroomId,
                "assignments", List.of(
                        Map.of(
                                "clientReference", "ready",
                                "studentId", student2.get("studentId").asText(),
                                "deviceId", pc01),
                        Map.of(
                                "clientReference", "occupied",
                                "studentId", student3.get("studentId").asText(),
                                "deviceId", pc02),
                        Map.of(
                                "clientReference", "internal-a",
                                "studentId", student4.get("studentId").asText(),
                                "deviceId", pc04),
                        Map.of(
                                "clientReference", "internal-b",
                                "studentId", student5.get("studentId").asText(),
                                "deviceId", pc04)));
        JsonNode batch = read(mockMvc.perform(post("/api/assignments/batch")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(assignmentBatchRequest)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.successCount").value(1))
                .andExpect(jsonPath("$.failedCount").value(3))
                .andReturn());
        assertThat(batch.get("operationId").asText()).isNotBlank();
        assertThat(batch.get("results").findValuesAsText("errorCode"))
                .contains(ErrorCode.TARGET_OCCUPIED.name());

        mockMvc.perform(get("/api/operations/{id}", batch.get("operationId").asText()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.type").value("ASSIGN_STUDENT"))
                .andExpect(jsonPath("$.targets.length()").value(4));

        JsonNode snapshot = read(mockMvc.perform(get("/api/classrooms/{id}/snapshot", classroomId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.summary.deviceCount").value(4))
                .andExpect(jsonPath("$.summary.assignedDeviceCount").value(2))
                .andExpect(jsonPath("$.summary.freeDeviceCount").value(2))
                .andReturn());
        JsonNode pc03Node = deviceByName(snapshot, "PC03");
        JsonNode pc01Node = deviceByName(snapshot, "PC01");
        assertThat(pc03Node.get("assignedStudentId").isNull()).isTrue();
        assertThat(pc01Node.get("assignedStudentDisplayName").asText()).isEqualTo("Mia Ortega");

        String retryOperationId = id();
        batchOperationService.create(
                classroomId,
                BatchOperation.fromTargets(
                        retryOperationId,
                        OperationType.OPEN_APPLICATION,
                        "LOCAL_MASTER",
                        OffsetDateTime.parse("2026-08-26T11:00:00Z"),
                        List.of(new BatchTargetResult(
                                new OperationTarget(OperationTargetType.DEVICE, pc03, "PC03"),
                                TargetExecutionStatus.FAILED,
                                ErrorCode.DEVICE_OFFLINE,
                                "offline",
                                1))),
                OperationPayload.none());

        mockMvc.perform(get("/api/operations"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[?(@.operationId=='" + retryOperationId + "')]").exists());
        mockMvc.perform(get("/api/operations/{id}/retryable-targets", retryOperationId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.length()").value(1))
                .andExpect(jsonPath("$[0].errorCode").value("DEVICE_OFFLINE"));
    }

    @Test
    void bootstrapReturnsAuthorizationStorageAndClassroomCounts() throws Exception {
        String applicationId = createApplication("Typing " + id());
        String classroomId = createClassroom("Aula bootstrap " + id(), Set.of(applicationId));
        String groupId = createGroup(classroomId, "4", "C");
        createStudent(classroomId, groupId, "Nora", "Mendez");

        mockMvc.perform(get("/api/master/bootstrap"))
                .andExpect(status().isOk())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_JSON))
                .andExpect(jsonPath("$.authorization.authorized").value(true))
                .andExpect(jsonPath("$.storage.status").value("READY"))
                .andExpect(jsonPath("$.classrooms[?(@.classroomId=='" + classroomId + "')].counts.activeStudentCount")
                        .value(1))
                .andExpect(jsonPath("$.classrooms[?(@.classroomId=='" + classroomId + "')].counts.applicationCount")
                        .value(1));

        mockMvc.perform(get("/api/applications"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[?(@.applicationId=='" + applicationId + "')]").exists());
        mockMvc.perform(get("/api/classrooms/{id}/applications", classroomId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.length()").value(1));
    }

    @Test
    void storageUnavailableMapsAdministrativeEndpointsTo503() throws Exception {
        storageState.mark(MasterStorageStatus.UNAVAILABLE, ErrorCode.MASTER_DATABASE_UNAVAILABLE.name());

        mockMvc.perform(get("/api/classrooms"))
                .andExpect(status().isServiceUnavailable())
                .andExpect(jsonPath("$.code").value("MASTER_DATABASE_UNAVAILABLE"));
    }

    private String createClassroom(String displayName) throws Exception {
        return createClassroom(displayName, Set.of());
    }

    private String createClassroom(String displayName, Set<String> applicationIds) throws Exception {
        JsonNode classroom = read(mockMvc.perform(post("/api/classrooms")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "displayName", displayName,
                                "authorizedApplicationIds", applicationIds))))
                .andExpect(status().isCreated())
                .andReturn());
        return classroom.get("classroomId").asText();
    }

    private String createGroup(String classroomId, String grade, String section) throws Exception {
        JsonNode group = read(mockMvc.perform(post("/api/classrooms/{id}/groups", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "grade", grade,
                                "section", section))))
                .andExpect(status().isCreated())
                .andReturn());
        return group.get("groupId").asText();
    }

    private JsonNode createStudent(
            String classroomId,
            String groupId,
            String firstName,
            String lastName) throws Exception {
        return read(mockMvc.perform(post("/api/classrooms/{id}/students", classroomId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(json(Map.of(
                                "groupId", groupId,
                                "firstName", firstName,
                                "lastName", lastName))))
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
                        OffsetDateTime.parse("2026-08-26T10:00:00Z"),
                        EnumSet.of(DeviceCapability.LOCAL_IPC, DeviceCapability.SESSION_AGENT),
                        null));
        return deviceId;
    }

    private String createApplication(String displayName) {
        String applicationId = id();
        applicationCatalogService.create(new ApplicationDefinition(
                applicationId,
                displayName,
                ApplicationType.EDUCATIONAL_CONTENT,
                ApplicationAvailability.REQUIRED,
                LaunchPolicy.ALLOWED));
        return applicationId;
    }

    private JsonNode read(org.springframework.test.web.servlet.MvcResult result) throws Exception {
        return objectMapper.readTree(result.getResponse().getContentAsString());
    }

    private JsonNode deviceByName(JsonNode snapshot, String displayName) {
        for (JsonNode device : snapshot.get("devices")) {
            if (displayName.equals(device.get("displayName").asText())) {
                return device;
            }
        }
        throw new AssertionError("Device not found in snapshot: " + displayName);
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
