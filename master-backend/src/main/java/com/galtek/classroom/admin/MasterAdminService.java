package com.galtek.classroom.admin;

import com.galtek.classroom.admin.AdminDtos.ApplicationResponse;
import com.galtek.classroom.admin.AdminDtos.ArchiveRequest;
import com.galtek.classroom.admin.AdminDtos.ArchiveStudentBatchItem;
import com.galtek.classroom.admin.AdminDtos.ArchiveStudentsBatchRequest;
import com.galtek.classroom.admin.AdminDtos.AssignStudentRequest;
import com.galtek.classroom.admin.AdminDtos.AssignmentBatchItem;
import com.galtek.classroom.admin.AdminDtos.AssignmentBatchRequest;
import com.galtek.classroom.admin.AdminDtos.AssignmentResponse;
import com.galtek.classroom.admin.AdminDtos.BatchItemResultResponse;
import com.galtek.classroom.admin.AdminDtos.BatchResultResponse;
import com.galtek.classroom.admin.AdminDtos.BootstrapResponse;
import com.galtek.classroom.admin.AdminDtos.ClassroomCounts;
import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.AdminDtos.ClassroomSnapshotResponse;
import com.galtek.classroom.admin.AdminDtos.ClassroomSummaryResponse;
import com.galtek.classroom.admin.AdminDtos.CloseAssignmentRequest;
import com.galtek.classroom.admin.AdminDtos.CreateClassroomRequest;
import com.galtek.classroom.admin.AdminDtos.CreateGroupRequest;
import com.galtek.classroom.admin.AdminDtos.CreateStudentRequest;
import com.galtek.classroom.admin.AdminDtos.DeviceResponse;
import com.galtek.classroom.admin.AdminDtos.GroupResponse;
import com.galtek.classroom.admin.AdminDtos.OperationResponse;
import com.galtek.classroom.admin.AdminDtos.OperationSummaryResponse;
import com.galtek.classroom.admin.AdminDtos.OperationTargetResponse;
import com.galtek.classroom.admin.AdminDtos.SnapshotSummary;
import com.galtek.classroom.admin.AdminDtos.StudentResponse;
import com.galtek.classroom.admin.AdminDtos.UpdateClassroomRequest;
import com.galtek.classroom.admin.AdminDtos.UpdateGroupRequest;
import com.galtek.classroom.admin.AdminDtos.UpdateStudentRequest;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientConnectionSnapshot;
import com.galtek.classroom.network.DeviceNetworkBindingRepository;
import com.galtek.classroom.network.RegisteredNetworkDevice;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageException;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.time.Clock;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import java.util.UUID;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class MasterAdminService {

    private static final String ASSIGNMENT_SOURCE_MANUAL = "MANUAL";

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterAdminRepository repository;
    private final DeviceNetworkBindingRepository deviceNetworkBindingRepository;
    private final ClientConnectionRegistry connectionRegistry;
    private final Clock clock;

    public MasterAdminService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterAdminRepository repository,
            DeviceNetworkBindingRepository deviceNetworkBindingRepository,
            ClientConnectionRegistry connectionRegistry,
            Clock clock) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.repository = repository;
        this.deviceNetworkBindingRepository = deviceNetworkBindingRepository;
        this.connectionRegistry = connectionRegistry;
        this.clock = clock;
    }

    public BootstrapResponse bootstrap() {
        MasterAuthorizationResponse authorization = requireAuthorizedAndStorage();
        return new BootstrapResponse(
                authorization,
                storageState.health(),
                repository.findClassroomSummaries(true));
    }

    public List<ClassroomSummaryResponse> classrooms(Boolean active) {
        requireAuthorizedAndStorage();
        return repository.findClassroomSummaries(active == null || active);
    }

    @Transactional
    public ClassroomResponse createClassroom(CreateClassroomRequest request) {
        requireAuthorizedAndStorage();
        String classroomId = id();
        repository.createClassroom(
                classroomId,
                required(request == null ? null : request.displayName(), "displayName"),
                cleanSet(request == null ? null : request.authorizedApplicationIds()),
                optional(request == null ? null : request.defaultBrowserProfileId()),
                request == null || request.workspaceRecoveryPlanned() == null
                        || request.workspaceRecoveryPlanned(),
                request == null || request.batchConfirmationsRequired() == null
                        || request.batchConfirmationsRequired(),
                nowUtc());
        return classroomOr404(classroomId);
    }

    @Transactional
    public ClassroomResponse updateClassroom(String classroomId, UpdateClassroomRequest request) {
        requireAuthorizedAndStorage();
        ClassroomResponse current = classroomOr404(classroomId);
        long expectedVersion = expectedVersion(request == null ? null : request.expectedVersion());
        repository.updateClassroom(
                classroomId,
                request == null || request.displayName() == null
                        ? current.displayName()
                        : required(request.displayName(), "displayName"),
                request == null || request.authorizedApplicationIds() == null
                        ? current.authorizedApplicationIds()
                        : cleanSet(request.authorizedApplicationIds()),
                request == null || request.defaultBrowserProfileId() == null
                        ? current.defaultBrowserProfileId()
                        : optional(request.defaultBrowserProfileId()),
                request == null || request.workspaceRecoveryPlanned() == null
                        ? current.workspaceRecoveryPlanned()
                        : request.workspaceRecoveryPlanned(),
                request == null || request.batchConfirmationsRequired() == null
                        ? current.batchConfirmationsRequired()
                        : request.batchConfirmationsRequired(),
                expectedVersion,
                nowUtc());
        return classroomOr404(classroomId);
    }

    @Transactional
    public ClassroomResponse archiveClassroom(String classroomId, ArchiveRequest request) {
        requireAuthorizedAndStorage();
        ClassroomResponse current = classroomOr404(classroomId);
        ClassroomCounts counts = current.counts();
        int activeContent = counts.groupCount()
                + counts.activeStudentCount()
                + counts.deviceCount()
                + counts.currentAssignmentCount()
                + counts.applicationCount();
        if (activeContent > 0) {
            throw conflict(
                    ErrorCode.CLASSROOM_HAS_ACTIVE_CONTENT,
                    "Classroom still has active groups, students, devices, assignments, or applications.");
        }

        repository.archiveClassroom(
                classroomId,
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return classroomOr404(classroomId);
    }

    public List<GroupResponse> groups(String classroomId, Boolean active) {
        requireAuthorizedAndStorage();
        classroomOr404(classroomId);
        return repository.findGroups(classroomId, active == null || active);
    }

    @Transactional
    public GroupResponse createGroup(String classroomId, CreateGroupRequest request) {
        requireAuthorizedAndStorage();
        classroomOr404(classroomId);
        String grade = required(request == null ? null : request.grade(), "grade");
        String section = required(request == null ? null : request.section(), "section");
        String groupId = id();
        repository.createGroup(
                classroomId,
                groupId,
                grade,
                section,
                request == null || request.displayName() == null
                        ? grade + " " + section
                        : required(request.displayName(), "displayName"),
                nowUtc());
        return groupOr404(groupId);
    }

    @Transactional
    public GroupResponse updateGroup(String groupId, UpdateGroupRequest request) {
        requireAuthorizedAndStorage();
        GroupResponse current = groupOr404(groupId);
        repository.updateGroup(
                groupId,
                request == null || request.grade() == null ? current.grade() : required(request.grade(), "grade"),
                request == null || request.section() == null
                        ? current.section()
                        : required(request.section(), "section"),
                request == null || request.displayName() == null
                        ? current.displayName()
                        : required(request.displayName(), "displayName"),
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return groupOr404(groupId);
    }

    @Transactional
    public GroupResponse archiveGroup(String groupId, ArchiveRequest request) {
        requireAuthorizedAndStorage();
        GroupResponse current = groupOr404(groupId);
        if (current.activeStudentCount() > 0) {
            throw conflict(
                    ErrorCode.GROUP_HAS_ACTIVE_STUDENTS,
                    "School group still has active students.");
        }

        repository.archiveGroup(
                groupId,
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return groupOr404(groupId);
    }

    public List<StudentResponse> students(
            String classroomId,
            String groupId,
            Boolean active,
            String search) {
        requireAuthorizedAndStorage();
        classroomOr404(classroomId);
        String cleanGroupId = optional(groupId);
        if (cleanGroupId != null) {
            GroupResponse group = groupOr404(cleanGroupId);
            if (!group.classroomId().equals(classroomId)) {
                throw validation("groupId belongs to a different classroom.");
            }
        }
        return repository.findStudents(classroomId, cleanGroupId, active == null || active, optional(search));
    }

    public StudentResponse student(String studentId) {
        requireAuthorizedAndStorage();
        return studentOr404(studentId);
    }

    @Transactional
    public StudentResponse createStudent(String classroomId, CreateStudentRequest request) {
        requireAuthorizedAndStorage();
        String studentId = createStudentInternal(classroomId, request);
        return studentOr404(studentId);
    }

    public BatchResultResponse createStudentsBatch(String classroomId, AdminDtos.BatchStudentsRequest request) {
        requireAuthorizedAndStorage();
        classroomOr404(classroomId);
        List<CreateStudentRequest> students = requireList(request == null ? null : request.students(), "students");
        List<BatchItemResultResponse> results = new ArrayList<>();

        for (int index = 0; index < students.size(); index++) {
            CreateStudentRequest student = students.get(index);
            String reference = clientReference(student == null ? null : student.clientReference(), index);
            try {
                String studentId = createStudentInternal(classroomId, student);
                results.add(BatchItemResultResponse.successStudent(reference, studentId));
            } catch (ApiException exception) {
                results.add(BatchItemResultResponse.failed(reference, exception.code(), exception.getMessage()));
            } catch (IllegalArgumentException exception) {
                results.add(BatchItemResultResponse.failed(
                        reference,
                        ErrorCode.INVALID_REQUEST.name(),
                        exception.getMessage()));
            } catch (MasterStorageException exception) {
                if (batchRecoverableStorageError(exception)) {
                    results.add(BatchItemResultResponse.failed(
                            reference,
                            exception.errorCode().name(),
                            "Student could not be created."));
                } else {
                    throw exception;
                }
            }
        }

        return batchResult(null, results);
    }

    @Transactional
    public StudentResponse updateStudent(String studentId, UpdateStudentRequest request) {
        requireAuthorizedAndStorage();
        StudentResponse current = studentOr404(studentId);
        String groupId = request == null || request.groupId() == null
                ? current.groupId()
                : required(request.groupId(), "groupId");
        GroupResponse group = groupOr404(groupId);
        if (!group.classroomId().equals(current.classroomId())) {
            throw validation("groupId belongs to a different classroom.");
        }

        String firstName = request == null || request.firstName() == null
                ? current.firstName()
                : required(request.firstName(), "firstName");
        String lastName = request == null || request.lastName() == null
                ? current.lastName()
                : required(request.lastName(), "lastName");
        String displayName = request == null || request.displayName() == null
                ? current.displayName()
                : required(request.displayName(), "displayName");
        String grade = request == null || request.grade() == null
                ? current.grade()
                : required(request.grade(), "grade");

        repository.updateStudent(
                studentId,
                groupId,
                firstName,
                lastName,
                displayName,
                grade,
                group.displayName(),
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return studentOr404(studentId);
    }

    @Transactional
    public StudentResponse archiveStudent(String studentId, ArchiveRequest request) {
        requireAuthorizedAndStorage();
        studentOr404(studentId);
        repository.archiveStudent(
                studentId,
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return studentOr404(studentId);
    }

    public BatchResultResponse archiveStudentsBatch(ArchiveStudentsBatchRequest request) {
        requireAuthorizedAndStorage();
        List<ArchiveStudentBatchItem> students = requireList(request == null ? null : request.students(), "students");
        List<BatchItemResultResponse> results = new ArrayList<>();

        for (int index = 0; index < students.size(); index++) {
            ArchiveStudentBatchItem item = students.get(index);
            String reference = clientReference(item == null ? null : item.clientReference(), index);
            try {
                String studentId = required(item == null ? null : item.studentId(), "studentId");
                studentOr404(studentId);
                repository.archiveStudent(
                        studentId,
                        expectedVersion(item == null ? null : item.expectedVersion()),
                        nowUtc());
                results.add(BatchItemResultResponse.successStudent(reference, studentId));
            } catch (ApiException exception) {
                results.add(BatchItemResultResponse.failed(reference, exception.code(), exception.getMessage()));
            } catch (IllegalArgumentException exception) {
                results.add(BatchItemResultResponse.failed(
                        reference,
                        ErrorCode.INVALID_REQUEST.name(),
                        exception.getMessage()));
            } catch (MasterStorageException exception) {
                if (batchRecoverableStorageError(exception)) {
                    results.add(BatchItemResultResponse.failed(
                            reference,
                            exception.errorCode().name(),
                            "Student could not be archived."));
                } else {
                    throw exception;
                }
            }
        }

        return batchResult(null, results);
    }

    public List<AssignmentResponse> assignments(String classroomId, Boolean current) {
        requireAuthorizedAndStorage();
        classroomOr404(classroomId);
        return repository.findAssignmentsByClassroom(classroomId, current != null && current);
    }

    @Transactional
    public AssignmentResponse createAssignment(AssignStudentRequest request) {
        requireAuthorizedAndStorage();
        String studentId = required(request == null ? null : request.studentId(), "studentId");
        String deviceId = required(request == null ? null : request.deviceId(), "deviceId");
        assertAssignmentReady(studentId, deviceId);

        String assignmentId = id();
        repository.createAssignment(assignmentId, studentId, deviceId, ASSIGNMENT_SOURCE_MANUAL, nowUtc());
        return assignmentOr404(assignmentId);
    }

    @Transactional
    public BatchResultResponse createAssignmentsBatch(AssignmentBatchRequest request) {
        requireAuthorizedAndStorage();
        String classroomId = required(request == null ? null : request.classroomId(), "classroomId");
        classroomOr404(classroomId);
        List<AssignmentBatchItem> assignments =
                requireList(request == null ? null : request.assignments(), "assignments");

        List<AssignmentPreflight> preflight = preflightAssignments(classroomId, assignments);
        OffsetDateTime nowUtc = nowUtc();
        List<BatchItemResultResponse> results = new ArrayList<>();
        List<OperationTargetResponse> operationTargets = new ArrayList<>();

        for (AssignmentPreflight item : preflight) {
            if (item.errorCode() != null) {
                results.add(BatchItemResultResponse.failed(
                        item.clientReference(),
                        item.errorCode().name(),
                        item.message()));
                operationTargets.add(operationTarget(item, null, "FAILED", item.errorCode(), item.message()));
                continue;
            }

            String assignmentId = id();
            repository.createAssignment(
                    assignmentId,
                    item.studentId(),
                    item.deviceId(),
                    ASSIGNMENT_SOURCE_MANUAL,
                    nowUtc);
            results.add(BatchItemResultResponse.successAssignment(item.clientReference(), assignmentId));
            operationTargets.add(operationTarget(item, assignmentId, "SUCCESS", null, "Assigned."));
        }

        String operationId = id();
        repository.createAssignmentOperation(
                operationId,
                classroomId,
                operationStatus(results),
                results.size(),
                operationTargets,
                nowUtc);

        return batchResult(operationId, results);
    }

    @Transactional
    public AssignmentResponse closeAssignment(String assignmentId, CloseAssignmentRequest request) {
        requireAuthorizedAndStorage();
        AssignmentResponse current = assignmentOr404(assignmentId);
        if (!current.current()) {
            throw conflict(ErrorCode.ASSIGNMENT_NOT_FOUND, "Assignment is not current.");
        }

        repository.closeAssignment(
                assignmentId,
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return assignmentOr404(assignmentId);
    }

    public List<ApplicationResponse> applications() {
        requireAuthorizedAndStorage();
        return repository.findApplications();
    }

    public List<ApplicationResponse> classroomApplications(String classroomId) {
        requireAuthorizedAndStorage();
        classroomOr404(classroomId);
        return repository.findClassroomApplications(classroomId);
    }

    public List<OperationSummaryResponse> operations() {
        requireAuthorizedAndStorage();
        return repository.findOperationSummaries();
    }

    public OperationResponse operation(String operationId) {
        requireAuthorizedAndStorage();
        return repository.findOperation(operationId)
                .orElseThrow(() -> notFound(ErrorCode.OPERATION_NOT_FOUND, "Operation was not found."));
    }

    public List<OperationTargetResponse> retryableTargets(String operationId) {
        requireAuthorizedAndStorage();
        operation(operationId);
        return repository.findRetryableOperationTargets(operationId);
    }

    public ClassroomSnapshotResponse snapshot(String classroomId) {
        requireAuthorizedAndStorage();
        ClassroomResponse classroom = classroomOr404(classroomId);
        List<GroupResponse> groups = repository.findGroups(classroomId, true);
        List<StudentResponse> students = repository.findStudents(classroomId, null, true, null);
        List<DeviceResponse> devices = applyLivePresence(repository.findDevicesByClassroom(classroomId));
        List<AssignmentResponse> currentAssignments = repository.findAssignmentsByClassroom(classroomId, true);
        List<ApplicationResponse> applications = repository.findClassroomApplications(classroomId);

        int assignedDeviceCount = (int) devices.stream()
                .filter(device -> device.assignedStudentId() != null)
                .count();
        SnapshotSummary summary = new SnapshotSummary(
                groups.size(),
                students.size(),
                students.size(),
                devices.size(),
                devices.size() - assignedDeviceCount,
                assignedDeviceCount,
                currentAssignments.size(),
                applications.size());

        return new ClassroomSnapshotResponse(
                classroom,
                groups,
                students,
                devices,
                currentAssignments,
                applications,
                summary);
    }

    private String createStudentInternal(String classroomId, CreateStudentRequest request) {
        ClassroomResponse classroom = classroomOr404(classroomId);
        if (!classroom.active()) {
            throw conflict(ErrorCode.CLASSROOM_HAS_ACTIVE_CONTENT, "Classroom is archived.");
        }

        String groupId = required(request == null ? null : request.groupId(), "groupId");
        GroupResponse group = groupOr404(groupId);
        if (!group.classroomId().equals(classroomId)) {
            throw validation("groupId belongs to a different classroom.");
        }
        if (!group.active()) {
            throw conflict(ErrorCode.GROUP_NOT_FOUND, "School group is archived.");
        }

        String studentId = id();
        String firstName = required(request == null ? null : request.firstName(), "firstName");
        String lastName = required(request == null ? null : request.lastName(), "lastName");

        repository.createStudent(
                classroomId,
                groupId,
                studentId,
                firstName,
                lastName,
                request == null || request.displayName() == null
                        ? defaultDisplayName(firstName, lastName)
                        : required(request.displayName(), "displayName"),
                request == null || request.grade() == null
                        ? group.grade()
                        : required(request.grade(), "grade"),
                group.displayName(),
                request == null || request.workspaceId() == null
                        ? "workspace-" + studentId
                        : required(request.workspaceId(), "workspaceId"),
                request == null || request.browserProfileId() == null
                        ? "browser-" + studentId
                        : required(request.browserProfileId(), "browserProfileId"),
                nowUtc());
        return studentId;
    }

    private void assertAssignmentReady(String studentId, String deviceId) {
        Map<String, StudentResponse> students = repository.findStudentsByIds(List.of(studentId));
        Map<String, DeviceResponse> devices = repository.findDevicesByIds(List.of(deviceId));
        StudentResponse student = students.get(studentId);
        DeviceResponse device = devices.get(deviceId);

        if (student == null || !student.active()) {
            throw notFound(ErrorCode.STUDENT_NOT_FOUND, "Student was not found.");
        }
        if (device == null || !device.active()) {
            throw notFound(ErrorCode.DEVICE_NOT_FOUND, "Device was not found.");
        }
        if (!student.classroomId().equals(device.classroomId())) {
            throw validation("studentId and deviceId must belong to the same classroom.");
        }

        AssignmentResponse studentAssignment = repository.findCurrentAssignmentsByStudentIds(List.of(studentId))
                .get(studentId);
        if (studentAssignment != null) {
            if (studentAssignment.deviceId().equals(deviceId)) {
                throw conflict(ErrorCode.SAME_DEVICE_ASSIGNMENT, "Student is already assigned to that device.");
            }
            throw conflict(ErrorCode.STUDENT_ALREADY_ASSIGNED, "Student already has a current assignment.");
        }

        AssignmentResponse deviceAssignment = repository.findCurrentAssignmentsByDeviceIds(List.of(deviceId))
                .get(deviceId);
        if (deviceAssignment != null) {
            throw conflict(ErrorCode.TARGET_OCCUPIED, "Device already has a current assignment.");
        }
    }

    private List<AssignmentPreflight> preflightAssignments(
            String classroomId,
            List<AssignmentBatchItem> assignments) {
        List<AssignmentPreflight> preflight = new ArrayList<>();
        Map<String, Long> studentCounts = assignments.stream()
                .filter(item -> item != null)
                .map(AssignmentBatchItem::studentId)
                .filter(value -> value != null && !value.isBlank())
                .collect(Collectors.groupingBy(Function.identity(), HashMap::new, Collectors.counting()));
        Map<String, Long> deviceCounts = assignments.stream()
                .filter(item -> item != null)
                .map(AssignmentBatchItem::deviceId)
                .filter(value -> value != null && !value.isBlank())
                .collect(Collectors.groupingBy(Function.identity(), HashMap::new, Collectors.counting()));
        List<String> studentIds = studentCounts.keySet().stream().toList();
        List<String> deviceIds = deviceCounts.keySet().stream().toList();
        Map<String, StudentResponse> students = repository.findStudentsByIds(studentIds);
        Map<String, DeviceResponse> devices = repository.findDevicesByIds(deviceIds);
        Map<String, AssignmentResponse> currentByStudent =
                repository.findCurrentAssignmentsByStudentIds(studentIds);
        Map<String, AssignmentResponse> currentByDevice =
                repository.findCurrentAssignmentsByDeviceIds(deviceIds);

        for (int index = 0; index < assignments.size(); index++) {
            AssignmentBatchItem item = assignments.get(index);
            String reference = clientReference(item == null ? null : item.clientReference(), index);
            String studentId = optional(item == null ? null : item.studentId());
            String deviceId = optional(item == null ? null : item.deviceId());
            if (studentId == null || deviceId == null) {
                preflight.add(AssignmentPreflight.failed(
                        index,
                        reference,
                        studentId,
                        deviceId,
                        ErrorCode.INVALID_REQUEST,
                        "studentId and deviceId are required."));
                continue;
            }

            StudentResponse student = students.get(studentId);
            DeviceResponse device = devices.get(deviceId);
            if (student == null || !student.active()) {
                preflight.add(AssignmentPreflight.failed(
                        index,
                        reference,
                        studentId,
                        deviceId,
                        ErrorCode.STUDENT_NOT_FOUND,
                        "Student was not found."));
                continue;
            }
            if (device == null || !device.active()) {
                preflight.add(AssignmentPreflight.failed(
                        index,
                        reference,
                        studentId,
                        deviceId,
                        ErrorCode.DEVICE_NOT_FOUND,
                        "Device was not found."));
                continue;
            }
            if (!student.classroomId().equals(classroomId) || !device.classroomId().equals(classroomId)) {
                preflight.add(AssignmentPreflight.failed(
                        index,
                        reference,
                        studentId,
                        deviceId,
                        ErrorCode.INVALID_REQUEST,
                        "Assignment item belongs to a different classroom."));
                continue;
            }
            if (studentCounts.getOrDefault(studentId, 0L) > 1) {
                preflight.add(AssignmentPreflight.failed(
                        index,
                        reference,
                        studentId,
                        deviceId,
                        ErrorCode.STUDENT_ALREADY_ASSIGNED,
                        "Batch contains more than one assignment for the same student."));
                continue;
            }
            if (deviceCounts.getOrDefault(deviceId, 0L) > 1) {
                preflight.add(AssignmentPreflight.failed(
                        index,
                        reference,
                        studentId,
                        deviceId,
                        ErrorCode.TARGET_OCCUPIED,
                        "Batch contains more than one assignment for the same device."));
                continue;
            }

            AssignmentResponse studentAssignment = currentByStudent.get(studentId);
            if (studentAssignment != null) {
                ErrorCode code = studentAssignment.deviceId().equals(deviceId)
                        ? ErrorCode.SAME_DEVICE_ASSIGNMENT
                        : ErrorCode.STUDENT_ALREADY_ASSIGNED;
                preflight.add(AssignmentPreflight.failed(
                        index,
                        reference,
                        studentId,
                        deviceId,
                        code,
                        "Student already has a current assignment."));
                continue;
            }

            if (currentByDevice.containsKey(deviceId)) {
                preflight.add(AssignmentPreflight.failed(
                        index,
                        reference,
                        studentId,
                        deviceId,
                        ErrorCode.TARGET_OCCUPIED,
                        "Device already has a current assignment."));
                continue;
            }

            preflight.add(AssignmentPreflight.ready(
                    index,
                    reference,
                    studentId,
                    students.get(studentId).displayName(),
                    deviceId,
                    devices.get(deviceId).displayName()));
        }

        return preflight;
    }

    private OperationTargetResponse operationTarget(
            AssignmentPreflight item,
            String assignmentId,
            String status,
            ErrorCode errorCode,
            String message) {
        String targetId = assignmentId == null
                ? "batch-item-" + item.index()
                : assignmentId;
        String displayName = item.studentDisplayName() != null && item.deviceDisplayName() != null
                ? item.studentDisplayName() + " -> " + item.deviceDisplayName()
                : item.clientReference();
        return new OperationTargetResponse(
                "STUDENT",
                targetId,
                displayName,
                status,
                errorCode == null ? null : errorCode.name(),
                message,
                1);
    }

    private String operationStatus(List<BatchItemResultResponse> results) {
        long failures = results.stream().filter(result -> "FAILED".equals(result.status())).count();
        if (failures == 0) {
            return "SUCCESS";
        }
        if (failures == results.size()) {
            return "FAILED";
        }
        return "PARTIAL_SUCCESS";
    }

    private BatchResultResponse batchResult(String operationId, List<BatchItemResultResponse> results) {
        int failed = (int) results.stream().filter(result -> "FAILED".equals(result.status())).count();
        return new BatchResultResponse(operationId, results.size(), results.size() - failed, failed, results);
    }

    private List<DeviceResponse> applyLivePresence(List<DeviceResponse> devices) {
        List<String> deviceIds = devices.stream()
                .map(DeviceResponse::deviceId)
                .toList();
        Map<String, RegisteredNetworkDevice> bindings = deviceNetworkBindingRepository.findCurrentByDeviceIds(deviceIds)
                .stream()
                .collect(Collectors.toMap(
                        RegisteredNetworkDevice::deviceId,
                        Function.identity(),
                        (left, right) -> left));
        Map<String, ClientConnectionSnapshot> liveByDevice = connectionRegistry.snapshots().stream()
                .filter(snapshot -> snapshot.deviceId() != null)
                .collect(Collectors.toMap(
                        ClientConnectionSnapshot::deviceId,
                        Function.identity(),
                        (left, right) -> left));

        return devices.stream()
                .map(device -> {
                    RegisteredNetworkDevice binding = bindings.get(device.deviceId());
                    if (binding == null) {
                        return device;
                    }

                    ClientConnectionSnapshot snapshot = liveByDevice.get(device.deviceId());
                    Set<String> capabilities = snapshot != null && !snapshot.capabilities().isEmpty()
                            ? capabilityNames(snapshot.capabilities())
                            : capabilityNames(binding.capabilities());
                    return new DeviceResponse(
                            device.deviceId(),
                            device.classroomId(),
                            device.installationId(),
                            device.displayName(),
                            device.hostname(),
                            snapshot == null ? "OFFLINE" : snapshot.status().name(),
                            snapshot == null ? fallbackLastSeen(device, binding) : lastSeen(snapshot),
                            capabilities,
                            device.assignedStudentId(),
                            device.assignedStudentDisplayName(),
                            device.active(),
                            device.version());
                })
                .toList();
    }

    private boolean batchRecoverableStorageError(MasterStorageException exception) {
        return exception.errorCode() == ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION
                || exception.errorCode() == ErrorCode.CONCURRENT_MODIFICATION;
    }

    private Set<String> capabilityNames(Set<com.galtek.classroom.device.DeviceCapability> capabilities) {
        if (capabilities == null || capabilities.isEmpty()) {
            return Set.of();
        }
        return capabilities.stream()
                .map(Enum::name)
                .collect(Collectors.toCollection(TreeSet::new));
    }

    private OffsetDateTime fallbackLastSeen(DeviceResponse device, RegisteredNetworkDevice binding) {
        return binding.lastConnectedAtUtc() == null ? device.lastSeenUtc() : binding.lastConnectedAtUtc();
    }

    private OffsetDateTime lastSeen(ClientConnectionSnapshot snapshot) {
        Instant signal = snapshot.lastHeartbeatUtc() == null
                ? (snapshot.disconnectedAtUtc() == null ? snapshot.connectedAtUtc() : snapshot.disconnectedAtUtc())
                : snapshot.lastHeartbeatUtc();
        return signal == null ? null : OffsetDateTime.ofInstant(signal, ZoneOffset.UTC);
    }

    private MasterAuthorizationResponse requireAuthorizedAndStorage() {
        MasterAuthorizationResponse authorization = masterAccessGuard.requireAuthorized();
        MasterStorageHealth health = storageState.health();
        if (health.status() != MasterStorageStatus.READY) {
            String code = health.errorCode() == null
                    ? ErrorCode.MASTER_DATABASE_UNAVAILABLE.name()
                    : health.errorCode();
            throw new ApiException(
                    HttpStatus.SERVICE_UNAVAILABLE,
                    code,
                    "Master storage is unavailable.");
        }

        return authorization;
    }

    private ClassroomResponse classroomOr404(String classroomId) {
        String cleanId = required(classroomId, "classroomId");
        return repository.findClassroom(cleanId)
                .orElseThrow(() -> notFound(ErrorCode.CLASSROOM_NOT_FOUND, "Classroom was not found."));
    }

    private GroupResponse groupOr404(String groupId) {
        String cleanId = required(groupId, "groupId");
        return repository.findGroup(cleanId)
                .orElseThrow(() -> notFound(ErrorCode.GROUP_NOT_FOUND, "School group was not found."));
    }

    private StudentResponse studentOr404(String studentId) {
        String cleanId = required(studentId, "studentId");
        return repository.findStudent(cleanId)
                .orElseThrow(() -> notFound(ErrorCode.STUDENT_NOT_FOUND, "Student was not found."));
    }

    private AssignmentResponse assignmentOr404(String assignmentId) {
        String cleanId = required(assignmentId, "assignmentId");
        return repository.findAssignment(cleanId)
                .orElseThrow(() -> notFound(ErrorCode.ASSIGNMENT_NOT_FOUND, "Assignment was not found."));
    }

    private ApiException notFound(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.NOT_FOUND, errorCode, message);
    }

    private ApiException conflict(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.CONFLICT, errorCode, message);
    }

    private ApiException validation(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, message);
    }

    private <T> List<T> requireList(List<T> values, String fieldName) {
        if (values == null || values.isEmpty()) {
            throw validation(fieldName + " must contain at least one item.");
        }
        return new ArrayList<>(values);
    }

    private Set<String> cleanSet(Set<String> values) {
        if (values == null) {
            return Set.of();
        }
        return values.stream()
                .map(value -> required(value, "authorizedApplicationIds"))
                .collect(Collectors.toCollection(LinkedHashSet::new));
    }

    private long expectedVersion(Long version) {
        if (version == null) {
            throw validation("expectedVersion is required.");
        }
        if (version < 0) {
            throw validation("expectedVersion cannot be negative.");
        }
        return version;
    }

    private String clientReference(String clientReference, int index) {
        String clean = optional(clientReference);
        return clean == null ? "row-" + (index + 1) : clean;
    }

    private String required(String value, String fieldName) {
        String clean = optional(value);
        if (clean == null) {
            throw validation(fieldName + " is required.");
        }
        return clean;
    }

    private String optional(String value) {
        if (value == null || value.isBlank()) {
            return null;
        }
        return value.trim();
    }

    private String defaultDisplayName(String firstName, String lastName) {
        return required(firstName, "firstName") + " " + required(lastName, "lastName");
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }

    private String id() {
        return UUID.randomUUID().toString();
    }

    private record AssignmentPreflight(
            int index,
            String clientReference,
            String studentId,
            String studentDisplayName,
            String deviceId,
            String deviceDisplayName,
            ErrorCode errorCode,
            String message) {

        static AssignmentPreflight ready(
                int index,
                String clientReference,
                String studentId,
                String studentDisplayName,
                String deviceId,
                String deviceDisplayName) {
            return new AssignmentPreflight(
                    index,
                    clientReference,
                    studentId,
                    studentDisplayName,
                    deviceId,
                    deviceDisplayName,
                    null,
                    null);
        }

        static AssignmentPreflight failed(
                int index,
                String clientReference,
                String studentId,
                String deviceId,
                ErrorCode errorCode,
                String message) {
            return new AssignmentPreflight(
                    index,
                    clientReference,
                    studentId,
                    null,
                    deviceId,
                    null,
                    errorCode,
                    message);
        }
    }
}
