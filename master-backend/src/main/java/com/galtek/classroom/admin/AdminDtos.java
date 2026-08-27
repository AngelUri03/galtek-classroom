package com.galtek.classroom.admin;

import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.persistence.MasterStorageHealth;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Set;

public final class AdminDtos {

    private AdminDtos() {
    }

    public record ClassroomCounts(
            int groupCount,
            int activeStudentCount,
            int archivedStudentCount,
            int deviceCount,
            int currentAssignmentCount,
            int applicationCount) {
    }

    public record ClassroomSummaryResponse(
            String classroomId,
            String displayName,
            boolean active,
            long version,
            ClassroomCounts counts) {
    }

    public record ClassroomResponse(
            String classroomId,
            String displayName,
            boolean active,
            Set<String> authorizedApplicationIds,
            String defaultBrowserProfileId,
            boolean workspaceRecoveryPlanned,
            boolean batchConfirmationsRequired,
            long version,
            ClassroomCounts counts) {
    }

    public record CreateClassroomRequest(
            String displayName,
            Set<String> authorizedApplicationIds,
            String defaultBrowserProfileId,
            Boolean workspaceRecoveryPlanned,
            Boolean batchConfirmationsRequired) {
    }

    public record UpdateClassroomRequest(
            String displayName,
            Set<String> authorizedApplicationIds,
            String defaultBrowserProfileId,
            Boolean workspaceRecoveryPlanned,
            Boolean batchConfirmationsRequired,
            Long expectedVersion) {
    }

    public record ArchiveRequest(Long expectedVersion) {
    }

    public record GroupResponse(
            String groupId,
            String classroomId,
            String grade,
            String section,
            String displayName,
            boolean active,
            int activeStudentCount,
            long version) {
    }

    public record CreateGroupRequest(
            String grade,
            String section,
            String displayName) {
    }

    public record UpdateGroupRequest(
            String grade,
            String section,
            String displayName,
            Long expectedVersion) {
    }

    public record StudentResponse(
            String studentId,
            String classroomId,
            String groupId,
            String groupDisplayName,
            String firstName,
            String lastName,
            String displayName,
            String grade,
            boolean active,
            String workspaceId,
            String browserProfileId,
            AssignmentResponse currentAssignment,
            List<AssignmentResponse> assignmentHistory,
            long version) {
    }

    public record CreateStudentRequest(
            String clientReference,
            String groupId,
            String firstName,
            String lastName,
            String displayName,
            String grade,
            String workspaceId,
            String browserProfileId) {
    }

    public record UpdateStudentRequest(
            String groupId,
            String firstName,
            String lastName,
            String displayName,
            String grade,
            Long expectedVersion) {
    }

    public record BatchStudentsRequest(List<CreateStudentRequest> students) {
    }

    public record ArchiveStudentBatchItem(
            String clientReference,
            String studentId,
            Long expectedVersion) {
    }

    public record ArchiveStudentsBatchRequest(List<ArchiveStudentBatchItem> students) {
    }

    public record BatchResultResponse(
            String operationId,
            int total,
            int successCount,
            int failedCount,
            List<BatchItemResultResponse> results) {
    }

    public record BatchItemResultResponse(
            String clientReference,
            String status,
            String studentId,
            String assignmentId,
            String errorCode,
            String message) {

        public static BatchItemResultResponse successStudent(String clientReference, String studentId) {
            return new BatchItemResultResponse(clientReference, "SUCCESS", studentId, null, null, null);
        }

        public static BatchItemResultResponse successAssignment(String clientReference, String assignmentId) {
            return new BatchItemResultResponse(clientReference, "SUCCESS", null, assignmentId, null, null);
        }

        public static BatchItemResultResponse failed(String clientReference, String errorCode, String message) {
            return new BatchItemResultResponse(clientReference, "FAILED", null, null, errorCode, message);
        }
    }

    public record AssignStudentRequest(
            String studentId,
            String deviceId) {
    }

    public record AssignmentBatchItem(
            String clientReference,
            String studentId,
            String deviceId) {
    }

    public record AssignmentBatchRequest(
            String classroomId,
            List<AssignmentBatchItem> assignments) {
    }

    public record CloseAssignmentRequest(Long expectedVersion) {
    }

    public record AssignmentResponse(
            String assignmentId,
            String studentId,
            String studentDisplayName,
            String deviceId,
            String deviceDisplayName,
            OffsetDateTime assignedAtUtc,
            OffsetDateTime endedAtUtc,
            String status,
            String source,
            boolean current,
            long version) {
    }

    public record DeviceResponse(
            String deviceId,
            String classroomId,
            String installationId,
            String displayName,
            String hostname,
            String status,
            OffsetDateTime lastSeenUtc,
            Set<String> capabilities,
            String assignedStudentId,
            String assignedStudentDisplayName,
            boolean active,
            long version) {
    }

    public record ApplicationResponse(
            String applicationId,
            String displayName,
            String type,
            String availability,
            String launchPolicy,
            boolean active,
            long version) {
    }

    public record OperationSummaryResponse(
            String operationId,
            String classroomId,
            String type,
            String requestedBy,
            OffsetDateTime createdAtUtc,
            String status,
            int targetCount,
            long version) {
    }

    public record OperationResponse(
            String operationId,
            String classroomId,
            String type,
            String requestedBy,
            OffsetDateTime createdAtUtc,
            String status,
            int targetCount,
            List<OperationTargetResponse> targets,
            long version) {
    }

    public record OperationTargetResponse(
            String targetType,
            String targetId,
            String targetDisplayName,
            String status,
            String errorCode,
            String message,
            int attempt) {
    }

    public record BootstrapResponse(
            MasterAuthorizationResponse authorization,
            MasterStorageHealth storage,
            List<ClassroomSummaryResponse> classrooms) {
    }

    public record SnapshotSummary(
            int groupCount,
            int studentCount,
            int activeStudentCount,
            int deviceCount,
            int freeDeviceCount,
            int assignedDeviceCount,
            int currentAssignmentCount,
            int applicationCount) {
    }

    public record ClassroomSnapshotResponse(
            ClassroomResponse classroom,
            List<GroupResponse> groups,
            List<StudentResponse> students,
            List<DeviceResponse> devices,
            List<AssignmentResponse> currentAssignments,
            List<ApplicationResponse> applications,
            SnapshotSummary summary) {
    }
}
