package com.galtek.classroom.admin;

import com.galtek.classroom.admin.AdminDtos.ApplicationResponse;
import com.galtek.classroom.admin.AdminDtos.AssignmentResponse;
import com.galtek.classroom.admin.AdminDtos.ClassroomCounts;
import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.AdminDtos.ClassroomSummaryResponse;
import com.galtek.classroom.admin.AdminDtos.DeviceResponse;
import com.galtek.classroom.admin.AdminDtos.GroupResponse;
import com.galtek.classroom.admin.AdminDtos.OperationResponse;
import com.galtek.classroom.admin.AdminDtos.OperationSummaryResponse;
import com.galtek.classroom.admin.AdminDtos.OperationTargetResponse;
import com.galtek.classroom.admin.AdminDtos.StudentResponse;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageException;
import com.galtek.classroom.persistence.sqlite.JsonText;
import com.galtek.classroom.persistence.sqlite.SqliteExceptionMapper;
import com.galtek.classroom.persistence.sqlite.SqliteJdbc;
import com.galtek.classroom.persistence.sqlite.UtcTimestamps;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.TreeSet;
import java.util.function.Supplier;
import java.util.stream.Collectors;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.core.RowMapper;
import org.springframework.stereotype.Repository;

@Repository
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class MasterAdminRepository {

    private static final String CLASSROOM_COUNTS_SELECT = """
            (SELECT COUNT(*) FROM school_groups g
             WHERE g.classroom_id = c.classroom_id AND g.active = 1) AS group_count,
            (SELECT COUNT(*) FROM students s
             WHERE s.classroom_id = c.classroom_id AND s.active = 1) AS active_student_count,
            (SELECT COUNT(*) FROM students s
             WHERE s.classroom_id = c.classroom_id AND s.active = 0) AS archived_student_count,
            (SELECT COUNT(*) FROM devices d
             WHERE d.classroom_id = c.classroom_id AND d.active = 1) AS device_count,
            (SELECT COUNT(*) FROM device_assignments da
             INNER JOIN devices d ON d.device_id = da.device_id
             WHERE d.classroom_id = c.classroom_id AND da.current = 1) AS current_assignment_count,
            (SELECT COUNT(*) FROM classroom_applications ca
             WHERE ca.classroom_id = c.classroom_id) AS application_count
            """;

    private static final String STUDENT_SELECT = """
            SELECT s.*,
                   sg.display_name AS group_display_name,
                   da.assignment_id AS current_assignment_id,
                   da.device_id AS current_device_id,
                   d.display_name AS current_device_display_name,
                   da.assigned_at_utc AS current_assigned_at_utc,
                   da.ended_at_utc AS current_ended_at_utc,
                   da.status AS current_assignment_status,
                   da.source AS current_assignment_source,
                   da.current AS current_assignment_current,
                   da.version AS current_assignment_version
            FROM students s
            INNER JOIN school_groups sg ON sg.group_id = s.school_group_id
            LEFT JOIN device_assignments da ON da.student_id = s.student_id AND da.current = 1
            LEFT JOIN devices d ON d.device_id = da.device_id
            """;

    private static final String ASSIGNMENT_SELECT = """
            SELECT da.*,
                   s.display_name AS student_display_name,
                   d.display_name AS device_display_name
            FROM device_assignments da
            INNER JOIN students s ON s.student_id = da.student_id
            INNER JOIN devices d ON d.device_id = da.device_id
            """;

    private static final RowMapper<OperationTargetResponse> OPERATION_TARGET_MAPPER = (rs, rowNum) ->
            new OperationTargetResponse(
                    rs.getString("target_type"),
                    rs.getString("target_id"),
                    rs.getString("target_display_name"),
                    rs.getString("status"),
                    rs.getString("error_code"),
                    rs.getString("message"),
                    rs.getInt("attempt"));

    private final JdbcTemplate jdbcTemplate;

    public MasterAdminRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    public List<ClassroomSummaryResponse> findClassroomSummaries(boolean active) {
        return query(() -> jdbcTemplate.query("""
                SELECT c.classroom_id, c.display_name, c.active, c.version,
                       %s
                FROM classrooms c
                WHERE c.active = ?
                ORDER BY c.display_name
                """.formatted(CLASSROOM_COUNTS_SELECT),
                (rs, rowNum) -> new ClassroomSummaryResponse(
                        rs.getString("classroom_id"),
                        rs.getString("display_name"),
                        SqliteJdbc.bool(rs, "active"),
                        rs.getLong("version"),
                        classroomCountsFromRow(rs)),
                SqliteJdbc.bool(active)));
    }

    public Optional<ClassroomResponse> findClassroom(String classroomId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                """
                SELECT c.*,
                       %s
                FROM classrooms c
                WHERE c.classroom_id = ?
                """.formatted(CLASSROOM_COUNTS_SELECT),
                (rs, rowNum) -> classroomFromRow(rs),
                classroomId));
    }

    public void createClassroom(
            String classroomId,
            String displayName,
            Set<String> authorizedApplicationIds,
            String defaultBrowserProfileId,
            boolean workspaceRecoveryPlanned,
            boolean batchConfirmationsRequired,
            OffsetDateTime nowUtc) {
        execute(() -> {
            jdbcTemplate.update("""
                    INSERT INTO classrooms (
                        classroom_id, display_name, active, default_browser_profile_id,
                        workspace_recovery_planned, batch_confirmations_required,
                        created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, 1, ?, ?, ?, ?, ?, 0)
                    """,
                    classroomId,
                    displayName,
                    defaultBrowserProfileId,
                    SqliteJdbc.bool(workspaceRecoveryPlanned),
                    SqliteJdbc.bool(batchConfirmationsRequired),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));
            replaceClassroomApplications(classroomId, authorizedApplicationIds, nowUtc);
        }, "Classroom could not be created.");
    }

    public void updateClassroom(
            String classroomId,
            String displayName,
            Set<String> authorizedApplicationIds,
            String defaultBrowserProfileId,
            boolean workspaceRecoveryPlanned,
            boolean batchConfirmationsRequired,
            long expectedVersion,
            OffsetDateTime nowUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE classrooms
                    SET display_name = ?,
                        default_browser_profile_id = ?,
                        workspace_recovery_planned = ?,
                        batch_confirmations_required = ?,
                        updated_at_utc = ?,
                        version = version + 1
                    WHERE classroom_id = ? AND version = ?
                    """,
                    displayName,
                    defaultBrowserProfileId,
                    SqliteJdbc.bool(workspaceRecoveryPlanned),
                    SqliteJdbc.bool(batchConfirmationsRequired),
                    UtcTimestamps.toText(nowUtc),
                    classroomId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Classroom was modified concurrently.");
            replaceClassroomApplications(classroomId, authorizedApplicationIds, nowUtc);
        }, "Classroom could not be updated.");
    }

    public void archiveClassroom(String classroomId, long expectedVersion, OffsetDateTime nowUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE classrooms
                    SET active = 0, updated_at_utc = ?, version = version + 1
                    WHERE classroom_id = ? AND version = ?
                    """,
                    UtcTimestamps.toText(nowUtc),
                    classroomId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Classroom was modified concurrently.");
        }, "Classroom could not be archived.");
    }

    public List<GroupResponse> findGroups(String classroomId, boolean active) {
        return query(() -> jdbcTemplate.query("""
                SELECT g.*,
                       (SELECT COUNT(*) FROM students s
                        WHERE s.school_group_id = g.group_id AND s.active = 1) AS active_student_count
                FROM school_groups g
                WHERE g.classroom_id = ? AND g.active = ?
                ORDER BY g.display_name
                """,
                (rs, rowNum) -> groupFromRow(rs),
                classroomId,
                SqliteJdbc.bool(active)));
    }

    public Optional<GroupResponse> findGroup(String groupId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                """
                SELECT g.*,
                       (SELECT COUNT(*) FROM students s
                        WHERE s.school_group_id = g.group_id AND s.active = 1) AS active_student_count
                FROM school_groups g
                WHERE g.group_id = ?
                """,
                (rs, rowNum) -> groupFromRow(rs),
                groupId));
    }

    public void createGroup(
            String classroomId,
            String groupId,
            String grade,
            String section,
            String displayName,
            OffsetDateTime nowUtc) {
        execute(() -> jdbcTemplate.update("""
                INSERT INTO school_groups (
                    group_id, classroom_id, grade, section, display_name,
                    active, created_at_utc, updated_at_utc, version
                ) VALUES (?, ?, ?, ?, ?, 1, ?, ?, 0)
                """,
                groupId,
                classroomId,
                grade,
                section,
                displayName,
                UtcTimestamps.toText(nowUtc),
                UtcTimestamps.toText(nowUtc)), "School group could not be created.");
    }

    public void updateGroup(
            String groupId,
            String grade,
            String section,
            String displayName,
            long expectedVersion,
            OffsetDateTime nowUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE school_groups
                    SET grade = ?, section = ?, display_name = ?, updated_at_utc = ?, version = version + 1
                    WHERE group_id = ? AND version = ?
                    """,
                    grade,
                    section,
                    displayName,
                    UtcTimestamps.toText(nowUtc),
                    groupId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "School group was modified concurrently.");
        }, "School group could not be updated.");
    }

    public void archiveGroup(String groupId, long expectedVersion, OffsetDateTime nowUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE school_groups
                    SET active = 0, updated_at_utc = ?, version = version + 1
                    WHERE group_id = ? AND version = ?
                    """,
                    UtcTimestamps.toText(nowUtc),
                    groupId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "School group was modified concurrently.");
        }, "School group could not be archived.");
    }

    public List<StudentResponse> findStudents(
            String classroomId,
            String groupId,
            boolean active,
            String search) {
        return query(() -> {
            StringBuilder sql = new StringBuilder(STUDENT_SELECT)
                    .append(" WHERE s.classroom_id = ? AND s.active = ?");
            List<Object> args = new ArrayList<>();
            args.add(classroomId);
            args.add(SqliteJdbc.bool(active));

            if (groupId != null) {
                sql.append(" AND s.school_group_id = ?");
                args.add(groupId);
            }
            if (search != null) {
                sql.append("""
                         AND (
                            lower(s.display_name) LIKE ?
                            OR lower(s.first_name) LIKE ?
                            OR lower(s.last_name) LIKE ?
                         )
                        """);
                String pattern = "%" + search.toLowerCase() + "%";
                args.add(pattern);
                args.add(pattern);
                args.add(pattern);
            }

            sql.append(" ORDER BY s.display_name");
            return jdbcTemplate.query(sql.toString(), (rs, rowNum) -> studentFromRow(rs, List.of()), args.toArray());
        });
    }

    public Optional<StudentResponse> findStudent(String studentId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                STUDENT_SELECT + " WHERE s.student_id = ?",
                (rs, rowNum) -> studentFromRow(rs, findAssignmentsByStudent(studentId)),
                studentId));
    }

    public Map<String, StudentResponse> findStudentsByIds(Collection<String> studentIds) {
        if (studentIds.isEmpty()) {
            return Map.of();
        }

        return query(() -> {
            List<String> ids = distinct(studentIds);
            List<StudentResponse> students = jdbcTemplate.query(
                    STUDENT_SELECT + " WHERE s.student_id IN (%s)".formatted(placeholders(ids.size())),
                    (rs, rowNum) -> studentFromRow(rs, List.of()),
                    ids.toArray());
            return students.stream()
                    .collect(Collectors.toMap(
                            StudentResponse::studentId,
                            student -> student,
                            (left, right) -> left,
                            LinkedHashMap::new));
        });
    }

    public void createStudent(
            String classroomId,
            String groupId,
            String studentId,
            String firstName,
            String lastName,
            String displayName,
            String grade,
            String groupName,
            String workspaceId,
            String browserProfileId,
            OffsetDateTime nowUtc) {
        execute(() -> jdbcTemplate.update("""
                INSERT INTO students (
                    student_id, classroom_id, school_group_id, first_name, last_name,
                    display_name, grade, group_name, active, workspace_id, browser_profile_id,
                    created_at_utc, updated_at_utc, version
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, ?, 0)
                """,
                studentId,
                classroomId,
                groupId,
                firstName,
                lastName,
                displayName,
                grade,
                groupName,
                workspaceId,
                browserProfileId,
                UtcTimestamps.toText(nowUtc),
                UtcTimestamps.toText(nowUtc)), "Student could not be created.");
    }

    public void updateStudent(
            String studentId,
            String groupId,
            String firstName,
            String lastName,
            String displayName,
            String grade,
            String groupName,
            long expectedVersion,
            OffsetDateTime nowUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE students
                    SET school_group_id = ?,
                        first_name = ?,
                        last_name = ?,
                        display_name = ?,
                        grade = ?,
                        group_name = ?,
                        updated_at_utc = ?,
                        version = version + 1
                    WHERE student_id = ? AND version = ?
                    """,
                    groupId,
                    firstName,
                    lastName,
                    displayName,
                    grade,
                    groupName,
                    UtcTimestamps.toText(nowUtc),
                    studentId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Student was modified concurrently.");
        }, "Student could not be updated.");
    }

    public void archiveStudent(String studentId, long expectedVersion, OffsetDateTime nowUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE students
                    SET active = 0, updated_at_utc = ?, version = version + 1
                    WHERE student_id = ? AND version = ?
                    """,
                    UtcTimestamps.toText(nowUtc),
                    studentId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Student was modified concurrently.");
        }, "Student could not be archived.");
    }

    public List<DeviceResponse> findDevicesByClassroom(String classroomId) {
        return query(() -> jdbcTemplate.query("""
                SELECT d.*,
                       da.student_id AS assigned_student_id,
                       s.display_name AS assigned_student_display_name
                FROM devices d
                LEFT JOIN device_assignments da ON da.device_id = d.device_id AND da.current = 1
                LEFT JOIN students s ON s.student_id = da.student_id
                WHERE d.classroom_id = ? AND d.active = 1
                ORDER BY d.display_name
                """,
                (rs, rowNum) -> deviceFromRow(rs),
                classroomId));
    }

    public Map<String, DeviceResponse> findDevicesByIds(Collection<String> deviceIds) {
        if (deviceIds.isEmpty()) {
            return Map.of();
        }

        return query(() -> {
            List<String> ids = distinct(deviceIds);
            List<DeviceResponse> devices = jdbcTemplate.query("""
                    SELECT d.*,
                           da.student_id AS assigned_student_id,
                           s.display_name AS assigned_student_display_name
                    FROM devices d
                    LEFT JOIN device_assignments da ON da.device_id = d.device_id AND da.current = 1
                    LEFT JOIN students s ON s.student_id = da.student_id
                    WHERE d.device_id IN (%s)
                    """.formatted(placeholders(ids.size())),
                    (rs, rowNum) -> deviceFromRow(rs),
                    ids.toArray());
            return devices.stream()
                    .collect(Collectors.toMap(
                            DeviceResponse::deviceId,
                            device -> device,
                            (left, right) -> left,
                            LinkedHashMap::new));
        });
    }

    public List<ApplicationResponse> findApplications() {
        return query(() -> jdbcTemplate.query("""
                SELECT *
                FROM application_definitions
                WHERE active = 1
                ORDER BY display_name
                """,
                (rs, rowNum) -> applicationFromRow(rs)));
    }

    public List<ApplicationResponse> findClassroomApplications(String classroomId) {
        return query(() -> jdbcTemplate.query("""
                SELECT a.*
                FROM application_definitions a
                INNER JOIN classroom_applications ca ON ca.application_id = a.application_id
                WHERE ca.classroom_id = ? AND a.active = 1
                ORDER BY a.display_name
                """,
                (rs, rowNum) -> applicationFromRow(rs),
                classroomId));
    }

    public List<AssignmentResponse> findAssignmentsByClassroom(String classroomId, boolean currentOnly) {
        return query(() -> jdbcTemplate.query("""
                %s
                WHERE d.classroom_id = ? %s
                ORDER BY da.assigned_at_utc
                """.formatted(ASSIGNMENT_SELECT, currentOnly ? "AND da.current = 1" : ""),
                (rs, rowNum) -> assignmentFromRow(rs),
                classroomId));
    }

    public Optional<AssignmentResponse> findAssignment(String assignmentId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                ASSIGNMENT_SELECT + " WHERE da.assignment_id = ?",
                (rs, rowNum) -> assignmentFromRow(rs),
                assignmentId));
    }

    public List<AssignmentResponse> findAssignmentsByStudent(String studentId) {
        return query(() -> jdbcTemplate.query(
                ASSIGNMENT_SELECT + " WHERE da.student_id = ? ORDER BY da.assigned_at_utc",
                (rs, rowNum) -> assignmentFromRow(rs),
                studentId));
    }

    public Map<String, AssignmentResponse> findCurrentAssignmentsByStudentIds(Collection<String> studentIds) {
        if (studentIds.isEmpty()) {
            return Map.of();
        }

        return query(() -> {
            List<String> ids = distinct(studentIds);
            List<AssignmentResponse> assignments = jdbcTemplate.query(
                    ASSIGNMENT_SELECT + " WHERE da.current = 1 AND da.student_id IN (%s)"
                            .formatted(placeholders(ids.size())),
                    (rs, rowNum) -> assignmentFromRow(rs),
                    ids.toArray());
            return assignments.stream()
                    .collect(Collectors.toMap(
                            AssignmentResponse::studentId,
                            assignment -> assignment,
                            (left, right) -> left,
                            LinkedHashMap::new));
        });
    }

    public Map<String, AssignmentResponse> findCurrentAssignmentsByDeviceIds(Collection<String> deviceIds) {
        if (deviceIds.isEmpty()) {
            return Map.of();
        }

        return query(() -> {
            List<String> ids = distinct(deviceIds);
            List<AssignmentResponse> assignments = jdbcTemplate.query(
                    ASSIGNMENT_SELECT + " WHERE da.current = 1 AND da.device_id IN (%s)"
                            .formatted(placeholders(ids.size())),
                    (rs, rowNum) -> assignmentFromRow(rs),
                    ids.toArray());
            return assignments.stream()
                    .collect(Collectors.toMap(
                            AssignmentResponse::deviceId,
                            assignment -> assignment,
                            (left, right) -> left,
                            LinkedHashMap::new));
        });
    }

    public void createAssignment(
            String assignmentId,
            String studentId,
            String deviceId,
            String source,
            OffsetDateTime nowUtc) {
        execute(() -> jdbcTemplate.update("""
                INSERT INTO device_assignments (
                    assignment_id, student_id, device_id, assigned_at_utc, ended_at_utc,
                    status, source, current, created_at_utc, updated_at_utc, version
                ) VALUES (?, ?, ?, ?, NULL, 'CURRENT', ?, 1, ?, ?, 0)
                """,
                assignmentId,
                studentId,
                deviceId,
                UtcTimestamps.toText(nowUtc),
                source,
                UtcTimestamps.toText(nowUtc),
                UtcTimestamps.toText(nowUtc)), "Device assignment could not be created.");
    }

    public void closeAssignment(String assignmentId, long expectedVersion, OffsetDateTime nowUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE device_assignments
                    SET status = 'ENDED',
                        current = 0,
                        ended_at_utc = ?,
                        updated_at_utc = ?,
                        version = version + 1
                    WHERE assignment_id = ? AND current = 1 AND version = ?
                    """,
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc),
                    assignmentId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Device assignment was modified concurrently.");
        }, "Device assignment could not be closed.");
    }

    public void createAssignmentOperation(
            String operationId,
            String classroomId,
            String status,
            int targetCount,
            List<OperationTargetResponse> targets,
            OffsetDateTime nowUtc) {
        execute(() -> {
            jdbcTemplate.update("""
                    INSERT INTO batch_operations (
                        operation_id, classroom_id, operation_type, requested_by, created_at_utc,
                        started_at_utc, completed_at_utc, status, target_count,
                        payload_schema_version, payload_json, version
                    ) VALUES (?, ?, 'ASSIGN_STUDENT', 'LOCAL_MASTER', ?, ?, ?, ?, ?, 1, NULL, 0)
                    """,
                    operationId,
                    classroomId,
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc),
                    status,
                    targetCount);
            for (OperationTargetResponse target : targets) {
                jdbcTemplate.update("""
                        INSERT INTO batch_target_results (
                            operation_id, target_type, target_id, target_display_name,
                            status, error_code, message, attempt, updated_at_utc
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """,
                        operationId,
                        target.targetType(),
                        target.targetId(),
                        target.targetDisplayName(),
                        target.status(),
                        target.errorCode(),
                        target.message(),
                        target.attempt(),
                        UtcTimestamps.toText(nowUtc));
            }
        }, "Assignment batch operation could not be recorded.");
    }

    public List<OperationSummaryResponse> findOperationSummaries() {
        return query(() -> jdbcTemplate.query("""
                SELECT *
                FROM batch_operations
                ORDER BY created_at_utc DESC
                """,
                (rs, rowNum) -> operationSummaryFromRow(rs)));
    }

    public Optional<OperationResponse> findOperation(String operationId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM batch_operations WHERE operation_id = ?",
                (rs, rowNum) -> operationFromRow(rs, findOperationTargets(operationId)),
                operationId));
    }

    public List<OperationTargetResponse> findRetryableOperationTargets(String operationId) {
        return query(() -> jdbcTemplate.query("""
                SELECT *
                FROM batch_target_results
                WHERE operation_id = ? AND status = 'FAILED' AND error_code IS NOT NULL
                ORDER BY target_display_name
                """,
                OPERATION_TARGET_MAPPER,
                operationId).stream()
                .filter(target -> {
                    try {
                        return ErrorCode.valueOf(target.errorCode()).retryable();
                    } catch (IllegalArgumentException exception) {
                        throw new MasterStorageException(
                                ErrorCode.MASTER_DATABASE_CORRUPT,
                                "Stored operation error code is unknown.",
                                exception);
                    }
                })
                .toList());
    }

    private List<OperationTargetResponse> findOperationTargets(String operationId) {
        return query(() -> jdbcTemplate.query(
                "SELECT * FROM batch_target_results WHERE operation_id = ? ORDER BY target_display_name",
                OPERATION_TARGET_MAPPER,
                operationId));
    }

    private ClassroomResponse classroomFromRow(ResultSet rs) throws SQLException {
        String classroomId = rs.getString("classroom_id");
        return new ClassroomResponse(
                classroomId,
                rs.getString("display_name"),
                SqliteJdbc.bool(rs, "active"),
                classroomApplicationIds(classroomId),
                rs.getString("default_browser_profile_id"),
                SqliteJdbc.bool(rs, "workspace_recovery_planned"),
                SqliteJdbc.bool(rs, "batch_confirmations_required"),
                rs.getLong("version"),
                classroomCountsFromRow(rs));
    }

    private Set<String> classroomApplicationIds(String classroomId) {
        return query(() -> new LinkedHashSet<>(jdbcTemplate.query(
                "SELECT application_id FROM classroom_applications WHERE classroom_id = ? ORDER BY application_id",
                (rs, rowNum) -> rs.getString("application_id"),
                classroomId)));
    }

    private void replaceClassroomApplications(
            String classroomId,
            Set<String> authorizedApplicationIds,
            OffsetDateTime nowUtc) {
        jdbcTemplate.update("DELETE FROM classroom_applications WHERE classroom_id = ?", classroomId);
        for (String applicationId : authorizedApplicationIds) {
            jdbcTemplate.update("""
                    INSERT INTO classroom_applications (classroom_id, application_id, created_at_utc)
                    VALUES (?, ?, ?)
                    """,
                    classroomId,
                    applicationId,
                    UtcTimestamps.toText(nowUtc));
        }
    }

    private ClassroomCounts classroomCountsFromRow(ResultSet rs) throws SQLException {
        return new ClassroomCounts(
                rs.getInt("group_count"),
                rs.getInt("active_student_count"),
                rs.getInt("archived_student_count"),
                rs.getInt("device_count"),
                rs.getInt("current_assignment_count"),
                rs.getInt("application_count"));
    }

    private GroupResponse groupFromRow(ResultSet rs) throws SQLException {
        return new GroupResponse(
                rs.getString("group_id"),
                rs.getString("classroom_id"),
                rs.getString("grade"),
                rs.getString("section"),
                rs.getString("display_name"),
                SqliteJdbc.bool(rs, "active"),
                rs.getInt("active_student_count"),
                rs.getLong("version"));
    }

    private StudentResponse studentFromRow(ResultSet rs, List<AssignmentResponse> history) throws SQLException {
        return new StudentResponse(
                rs.getString("student_id"),
                rs.getString("classroom_id"),
                rs.getString("school_group_id"),
                rs.getString("group_display_name"),
                rs.getString("first_name"),
                rs.getString("last_name"),
                rs.getString("display_name"),
                rs.getString("grade"),
                SqliteJdbc.bool(rs, "active"),
                rs.getString("workspace_id"),
                rs.getString("browser_profile_id"),
                currentAssignmentFromStudentRow(rs),
                history,
                rs.getLong("version"));
    }

    private AssignmentResponse currentAssignmentFromStudentRow(ResultSet rs) throws SQLException {
        String assignmentId = rs.getString("current_assignment_id");
        if (assignmentId == null) {
            return null;
        }

        return new AssignmentResponse(
                assignmentId,
                rs.getString("student_id"),
                rs.getString("display_name"),
                rs.getString("current_device_id"),
                rs.getString("current_device_display_name"),
                UtcTimestamps.fromText(rs.getString("current_assigned_at_utc")),
                UtcTimestamps.fromText(rs.getString("current_ended_at_utc")),
                rs.getString("current_assignment_status"),
                rs.getString("current_assignment_source"),
                SqliteJdbc.bool(rs, "current_assignment_current"),
                rs.getLong("current_assignment_version"));
    }

    private DeviceResponse deviceFromRow(ResultSet rs) throws SQLException {
        Set<String> capabilities = JsonText.enumSet(rs.getString("capabilities_json"), DeviceCapability.class)
                .stream()
                .map(Enum::name)
                .collect(Collectors.toCollection(TreeSet::new));
        return new DeviceResponse(
                rs.getString("device_id"),
                rs.getString("classroom_id"),
                rs.getString("installation_id"),
                rs.getString("display_name"),
                rs.getString("hostname"),
                rs.getString("status"),
                UtcTimestamps.fromText(rs.getString("last_seen_utc")),
                capabilities,
                rs.getString("assigned_student_id"),
                rs.getString("assigned_student_display_name"),
                SqliteJdbc.bool(rs, "active"),
                rs.getLong("version"));
    }

    private ApplicationResponse applicationFromRow(ResultSet rs) throws SQLException {
        return new ApplicationResponse(
                rs.getString("application_id"),
                rs.getString("display_name"),
                rs.getString("type"),
                rs.getString("availability"),
                rs.getString("launch_policy"),
                SqliteJdbc.bool(rs, "active"),
                rs.getLong("version"));
    }

    private AssignmentResponse assignmentFromRow(ResultSet rs) throws SQLException {
        return new AssignmentResponse(
                rs.getString("assignment_id"),
                rs.getString("student_id"),
                rs.getString("student_display_name"),
                rs.getString("device_id"),
                rs.getString("device_display_name"),
                UtcTimestamps.fromText(rs.getString("assigned_at_utc")),
                UtcTimestamps.fromText(rs.getString("ended_at_utc")),
                rs.getString("status"),
                rs.getString("source"),
                SqliteJdbc.bool(rs, "current"),
                rs.getLong("version"));
    }

    private OperationSummaryResponse operationSummaryFromRow(ResultSet rs) throws SQLException {
        return new OperationSummaryResponse(
                rs.getString("operation_id"),
                rs.getString("classroom_id"),
                rs.getString("operation_type"),
                rs.getString("requested_by"),
                UtcTimestamps.fromText(rs.getString("created_at_utc")),
                rs.getString("status"),
                rs.getInt("target_count"),
                rs.getLong("version"));
    }

    private OperationResponse operationFromRow(
            ResultSet rs,
            List<OperationTargetResponse> targets) throws SQLException {
        return new OperationResponse(
                rs.getString("operation_id"),
                rs.getString("classroom_id"),
                rs.getString("operation_type"),
                rs.getString("requested_by"),
                UtcTimestamps.fromText(rs.getString("created_at_utc")),
                rs.getString("status"),
                rs.getInt("target_count"),
                targets,
                rs.getLong("version"));
    }

    private <T> T query(Supplier<T> query) {
        try {
            return query.get();
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Master administrative query failed.", exception);
        }
    }

    private void execute(Runnable runnable, String message) {
        try {
            runnable.run();
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map(message, exception);
        }
    }

    private List<String> distinct(Collection<String> values) {
        return values.stream()
                .filter(value -> value != null && !value.isBlank())
                .distinct()
                .toList();
    }

    private String placeholders(int count) {
        return String.join(",", Collections.nCopies(count, "?"));
    }
}
