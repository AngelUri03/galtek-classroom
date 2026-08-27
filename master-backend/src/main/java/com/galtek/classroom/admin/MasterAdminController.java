package com.galtek.classroom.admin;

import com.galtek.classroom.admin.AdminDtos.ApplicationResponse;
import com.galtek.classroom.admin.AdminDtos.ArchiveRequest;
import com.galtek.classroom.admin.AdminDtos.ArchiveStudentsBatchRequest;
import com.galtek.classroom.admin.AdminDtos.AssignStudentRequest;
import com.galtek.classroom.admin.AdminDtos.AssignmentBatchRequest;
import com.galtek.classroom.admin.AdminDtos.AssignmentResponse;
import com.galtek.classroom.admin.AdminDtos.BatchResultResponse;
import com.galtek.classroom.admin.AdminDtos.BootstrapResponse;
import com.galtek.classroom.admin.AdminDtos.ClassroomSnapshotResponse;
import com.galtek.classroom.admin.AdminDtos.ClassroomSummaryResponse;
import com.galtek.classroom.admin.AdminDtos.CloseAssignmentRequest;
import com.galtek.classroom.admin.AdminDtos.CreateClassroomRequest;
import com.galtek.classroom.admin.AdminDtos.CreateGroupRequest;
import com.galtek.classroom.admin.AdminDtos.CreateStudentRequest;
import com.galtek.classroom.admin.AdminDtos.GroupResponse;
import com.galtek.classroom.admin.AdminDtos.OperationResponse;
import com.galtek.classroom.admin.AdminDtos.OperationSummaryResponse;
import com.galtek.classroom.admin.AdminDtos.OperationTargetResponse;
import com.galtek.classroom.admin.AdminDtos.StudentResponse;
import com.galtek.classroom.admin.AdminDtos.UpdateClassroomRequest;
import com.galtek.classroom.admin.AdminDtos.UpdateGroupRequest;
import com.galtek.classroom.admin.AdminDtos.UpdateStudentRequest;
import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PatchMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api")
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class MasterAdminController {

    private final MasterAdminService service;

    public MasterAdminController(MasterAdminService service) {
        this.service = service;
    }

    @GetMapping("/master/bootstrap")
    public BootstrapResponse bootstrap() {
        return service.bootstrap();
    }

    @GetMapping("/classrooms")
    public List<ClassroomSummaryResponse> classrooms(@RequestParam(required = false) Boolean active) {
        return service.classrooms(active);
    }

    @PostMapping("/classrooms")
    public ResponseEntity<AdminDtos.ClassroomResponse> createClassroom(
            @RequestBody(required = false) CreateClassroomRequest request) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.createClassroom(request));
    }

    @PatchMapping("/classrooms/{id}")
    public AdminDtos.ClassroomResponse updateClassroom(
            @PathVariable String id,
            @RequestBody(required = false) UpdateClassroomRequest request) {
        return service.updateClassroom(id, request);
    }

    @PostMapping("/classrooms/{id}/archive")
    public AdminDtos.ClassroomResponse archiveClassroom(
            @PathVariable String id,
            @RequestBody(required = false) ArchiveRequest request) {
        return service.archiveClassroom(id, request);
    }

    @GetMapping("/classrooms/{id}/groups")
    public List<GroupResponse> groups(
            @PathVariable String id,
            @RequestParam(required = false) Boolean active) {
        return service.groups(id, active);
    }

    @PostMapping("/classrooms/{id}/groups")
    public ResponseEntity<GroupResponse> createGroup(
            @PathVariable String id,
            @RequestBody(required = false) CreateGroupRequest request) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.createGroup(id, request));
    }

    @PatchMapping("/groups/{id}")
    public GroupResponse updateGroup(
            @PathVariable String id,
            @RequestBody(required = false) UpdateGroupRequest request) {
        return service.updateGroup(id, request);
    }

    @PostMapping("/groups/{id}/archive")
    public GroupResponse archiveGroup(
            @PathVariable String id,
            @RequestBody(required = false) ArchiveRequest request) {
        return service.archiveGroup(id, request);
    }

    @GetMapping("/classrooms/{id}/students")
    public List<StudentResponse> students(
            @PathVariable String id,
            @RequestParam(required = false) String groupId,
            @RequestParam(required = false) Boolean active,
            @RequestParam(required = false) String search) {
        return service.students(id, groupId, active, search);
    }

    @GetMapping("/students/{id}")
    public StudentResponse student(@PathVariable String id) {
        return service.student(id);
    }

    @PostMapping("/classrooms/{id}/students")
    public ResponseEntity<StudentResponse> createStudent(
            @PathVariable String id,
            @RequestBody(required = false) CreateStudentRequest request) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.createStudent(id, request));
    }

    @PostMapping("/classrooms/{id}/students/batch")
    public BatchResultResponse createStudentsBatch(
            @PathVariable String id,
            @RequestBody(required = false) AdminDtos.BatchStudentsRequest request) {
        return service.createStudentsBatch(id, request);
    }

    @PatchMapping("/students/{id}")
    public StudentResponse updateStudent(
            @PathVariable String id,
            @RequestBody(required = false) UpdateStudentRequest request) {
        return service.updateStudent(id, request);
    }

    @PostMapping("/students/{id}/archive")
    public StudentResponse archiveStudent(
            @PathVariable String id,
            @RequestBody(required = false) ArchiveRequest request) {
        return service.archiveStudent(id, request);
    }

    @PostMapping("/students/archive-batch")
    public BatchResultResponse archiveStudentsBatch(
            @RequestBody(required = false) ArchiveStudentsBatchRequest request) {
        return service.archiveStudentsBatch(request);
    }

    @GetMapping("/classrooms/{id}/assignments")
    public List<AssignmentResponse> assignments(
            @PathVariable String id,
            @RequestParam(required = false) Boolean current) {
        return service.assignments(id, current);
    }

    @PostMapping("/assignments")
    public ResponseEntity<AssignmentResponse> createAssignment(
            @RequestBody(required = false) AssignStudentRequest request) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.createAssignment(request));
    }

    @PostMapping("/assignments/batch")
    public BatchResultResponse createAssignmentsBatch(
            @RequestBody(required = false) AssignmentBatchRequest request) {
        return service.createAssignmentsBatch(request);
    }

    @PostMapping("/assignments/{id}/close")
    public AssignmentResponse closeAssignment(
            @PathVariable String id,
            @RequestBody(required = false) CloseAssignmentRequest request) {
        return service.closeAssignment(id, request);
    }

    @GetMapping("/applications")
    public List<ApplicationResponse> applications() {
        return service.applications();
    }

    @GetMapping("/classrooms/{id}/applications")
    public List<ApplicationResponse> classroomApplications(@PathVariable String id) {
        return service.classroomApplications(id);
    }

    @GetMapping("/operations")
    public List<OperationSummaryResponse> operations() {
        return service.operations();
    }

    @GetMapping("/operations/{id}")
    public OperationResponse operation(@PathVariable String id) {
        return service.operation(id);
    }

    @GetMapping("/operations/{id}/retryable-targets")
    public List<OperationTargetResponse> retryableTargets(@PathVariable String id) {
        return service.retryableTargets(id);
    }

    @GetMapping("/classrooms/{id}/snapshot")
    public ClassroomSnapshotResponse snapshot(@PathVariable String id) {
        return service.snapshot(id);
    }
}
