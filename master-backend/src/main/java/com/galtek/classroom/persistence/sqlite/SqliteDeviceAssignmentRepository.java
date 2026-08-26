package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.student.DeviceAssignment;
import com.galtek.classroom.student.DeviceAssignmentRecord;
import com.galtek.classroom.student.DeviceAssignmentRepository;
import com.galtek.classroom.student.DeviceAssignmentSource;
import com.galtek.classroom.student.DeviceAssignmentStatus;
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
public class SqliteDeviceAssignmentRepository implements DeviceAssignmentRepository {

    private static final RowMapper<DeviceAssignmentRecord> ROW_MAPPER = (rs, rowNum) -> new DeviceAssignmentRecord(
            rs.getString("assignment_id"),
            new DeviceAssignment(
                    rs.getString("student_id"),
                    rs.getString("device_id"),
                    UtcTimestamps.fromText(rs.getString("assigned_at_utc")),
                    DeviceAssignmentStatus.valueOf(rs.getString("status")),
                    DeviceAssignmentSource.valueOf(rs.getString("source")),
                    SqliteJdbc.bool(rs, "current")),
            UtcTimestamps.fromText(rs.getString("ended_at_utc")),
            UtcTimestamps.fromText(rs.getString("created_at_utc")),
            UtcTimestamps.fromText(rs.getString("updated_at_utc")),
            rs.getLong("version"));

    private final JdbcTemplate jdbcTemplate;

    public SqliteDeviceAssignmentRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(String assignmentId, DeviceAssignment assignment, OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO device_assignments (
                        assignment_id, student_id, device_id, assigned_at_utc, ended_at_utc,
                        status, source, current, created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, ?, ?, NULL, ?, ?, ?, ?, ?, 0)
                    """,
                    assignmentId,
                    assignment.studentId(),
                    assignment.deviceId(),
                    UtcTimestamps.toText(assignment.assignedAtUtc()),
                    assignment.status().name(),
                    assignment.source().name(),
                    SqliteJdbc.bool(assignment.current()),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Device assignment could not be created.", exception);
        }
    }

    @Override
    public Optional<DeviceAssignmentRecord> findCurrentByStudentId(String studentId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM device_assignments WHERE student_id = ? AND current = 1",
                ROW_MAPPER,
                studentId);
    }

    @Override
    public Optional<DeviceAssignmentRecord> findCurrentByDeviceId(String deviceId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM device_assignments WHERE device_id = ? AND current = 1",
                ROW_MAPPER,
                deviceId);
    }

    @Override
    public List<DeviceAssignmentRecord> findCurrentByClassroomId(String classroomId) {
        return jdbcTemplate.query("""
                SELECT da.*
                FROM device_assignments da
                INNER JOIN devices d ON d.device_id = da.device_id
                WHERE d.classroom_id = ? AND da.current = 1
                ORDER BY da.assigned_at_utc
                """,
                ROW_MAPPER,
                classroomId);
    }

    @Override
    public List<DeviceAssignmentRecord> findHistoryByStudentId(String studentId) {
        return jdbcTemplate.query(
                "SELECT * FROM device_assignments WHERE student_id = ? ORDER BY assigned_at_utc",
                ROW_MAPPER,
                studentId);
    }

    @Override
    public void endAssignment(String assignmentId, OffsetDateTime endedAtUtc) {
        try {
            int updatedRows = jdbcTemplate.update("""
                    UPDATE device_assignments
                    SET status = 'ENDED', current = 0, ended_at_utc = ?, updated_at_utc = ?, version = version + 1
                    WHERE assignment_id = ? AND current = 1
                    """,
                    UtcTimestamps.toText(endedAtUtc),
                    UtcTimestamps.toText(endedAtUtc),
                    assignmentId);
            SqliteJdbc.requireUpdated(updatedRows, "Device assignment was not current.");
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Device assignment could not be ended.", exception);
        }
    }
}
