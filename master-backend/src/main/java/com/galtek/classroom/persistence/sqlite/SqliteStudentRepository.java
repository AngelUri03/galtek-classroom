package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.student.DeviceAssignment;
import com.galtek.classroom.student.DeviceAssignmentSource;
import com.galtek.classroom.student.DeviceAssignmentStatus;
import com.galtek.classroom.student.Student;
import com.galtek.classroom.student.StudentRepository;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;
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
public class SqliteStudentRepository implements StudentRepository {

    private static final String STUDENT_SELECT = """
            SELECT s.*,
                   da.device_id AS current_device_id,
                   da.assigned_at_utc AS current_assigned_at_utc,
                   da.status AS current_status,
                   da.source AS current_source,
                   da.current AS current_assignment
            FROM students s
            LEFT JOIN device_assignments da ON da.student_id = s.student_id AND da.current = 1
            """;

    private static final RowMapper<Student> ROW_MAPPER = (rs, rowNum) -> {
        DeviceAssignment assignment = null;
        String currentDeviceId = rs.getString("current_device_id");
        if (currentDeviceId != null) {
            assignment = new DeviceAssignment(
                    rs.getString("student_id"),
                    currentDeviceId,
                    UtcTimestamps.fromText(rs.getString("current_assigned_at_utc")),
                    DeviceAssignmentStatus.valueOf(rs.getString("current_status")),
                    DeviceAssignmentSource.valueOf(rs.getString("current_source")),
                    SqliteJdbc.bool(rs, "current_assignment"));
        }

        return new Student(
                rs.getString("student_id"),
                rs.getString("first_name"),
                rs.getString("last_name"),
                rs.getString("display_name"),
                rs.getString("grade"),
                rs.getString("group_name"),
                SqliteJdbc.bool(rs, "active"),
                rs.getString("workspace_id"),
                rs.getString("browser_profile_id"),
                assignment);
    };

    private final JdbcTemplate jdbcTemplate;

    public SqliteStudentRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(String classroomId, String schoolGroupId, Student student, OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO students (
                        student_id, classroom_id, school_group_id, first_name, last_name,
                        display_name, grade, group_name, active, workspace_id, browser_profile_id,
                        created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)
                    """,
                    student.studentId(),
                    classroomId,
                    schoolGroupId,
                    student.firstName(),
                    student.lastName(),
                    student.displayName(),
                    student.grade(),
                    student.group(),
                    SqliteJdbc.bool(student.active()),
                    student.workspaceId(),
                    student.browserProfileId(),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Student could not be created.", exception);
        }
    }

    @Override
    public Optional<Student> findById(String studentId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                STUDENT_SELECT + " WHERE s.student_id = ?",
                ROW_MAPPER,
                studentId);
    }

    @Override
    public List<Student> findByGroupId(String schoolGroupId) {
        return jdbcTemplate.query(
                STUDENT_SELECT + " WHERE s.school_group_id = ? ORDER BY s.display_name",
                ROW_MAPPER,
                schoolGroupId);
    }

    @Override
    public List<Student> findActiveByClassroomId(String classroomId) {
        return jdbcTemplate.query(
                STUDENT_SELECT + " WHERE s.classroom_id = ? AND s.active = 1 ORDER BY s.display_name",
                ROW_MAPPER,
                classroomId);
    }

    @Override
    public long versionOf(String studentId) {
        Long version = jdbcTemplate.queryForObject(
                "SELECT version FROM students WHERE student_id = ?",
                Long.class,
                studentId);
        return version == null ? 0 : version;
    }

    @Override
    public void archive(String studentId, long expectedVersion, OffsetDateTime updatedAtUtc) {
        try {
            int updatedRows = jdbcTemplate.update("""
                    UPDATE students
                    SET active = 0, updated_at_utc = ?, version = version + 1
                    WHERE student_id = ? AND version = ?
                    """,
                    UtcTimestamps.toText(updatedAtUtc),
                    studentId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updatedRows, "Student was modified concurrently.");
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Student could not be archived.", exception);
        }
    }
}
