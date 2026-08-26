package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.classroom.Classroom;
import com.galtek.classroom.classroom.ClassroomConfiguration;
import com.galtek.classroom.classroom.ClassroomRepository;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;

@Repository
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class SqliteClassroomRepository implements ClassroomRepository {

    private final JdbcTemplate jdbcTemplate;

    public SqliteClassroomRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(Classroom classroom, OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO classrooms (
                        classroom_id, display_name, active, default_browser_profile_id,
                        workspace_recovery_planned, batch_confirmations_required,
                        created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, 1, ?, ?, ?, ?, ?, 0)
                    """,
                    classroom.classroomId(),
                    classroom.displayName(),
                    classroom.configuration().defaultBrowserProfileId(),
                    SqliteJdbc.bool(classroom.configuration().workspaceRecoveryPlanned()),
                    SqliteJdbc.bool(classroom.configuration().batchConfirmationsRequired()),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));

            for (String applicationId : classroom.configuration().authorizedApplicationIds()) {
                authorizeApplication(classroom.classroomId(), applicationId, nowUtc);
            }
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Classroom could not be created.", exception);
        }
    }

    @Override
    public Optional<Classroom> findById(String classroomId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM classrooms WHERE classroom_id = ?",
                (rs, rowNum) -> classroomFromRow(rs.getString("classroom_id"), rs.getString("display_name"),
                        rs.getString("default_browser_profile_id"),
                        SqliteJdbc.bool(rs, "workspace_recovery_planned"),
                        SqliteJdbc.bool(rs, "batch_confirmations_required")),
                classroomId);
    }

    @Override
    public List<Classroom> findActive() {
        return jdbcTemplate.query(
                "SELECT * FROM classrooms WHERE active = 1 ORDER BY display_name",
                (rs, rowNum) -> classroomFromRow(rs.getString("classroom_id"), rs.getString("display_name"),
                        rs.getString("default_browser_profile_id"),
                        SqliteJdbc.bool(rs, "workspace_recovery_planned"),
                        SqliteJdbc.bool(rs, "batch_confirmations_required")));
    }

    @Override
    public long versionOf(String classroomId) {
        Long version = jdbcTemplate.queryForObject(
                "SELECT version FROM classrooms WHERE classroom_id = ?",
                Long.class,
                classroomId);
        return version == null ? 0 : version;
    }

    @Override
    public void updateDisplayName(
            String classroomId,
            String displayName,
            long expectedVersion,
            OffsetDateTime updatedAtUtc) {
        try {
            int updatedRows = jdbcTemplate.update("""
                    UPDATE classrooms
                    SET display_name = ?, updated_at_utc = ?, version = version + 1
                    WHERE classroom_id = ? AND version = ?
                    """,
                    displayName,
                    UtcTimestamps.toText(updatedAtUtc),
                    classroomId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updatedRows, "Classroom was modified concurrently.");
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Classroom could not be updated.", exception);
        }
    }

    @Override
    public void archive(String classroomId, long expectedVersion, OffsetDateTime updatedAtUtc) {
        try {
            int updatedRows = jdbcTemplate.update("""
                    UPDATE classrooms
                    SET active = 0, updated_at_utc = ?, version = version + 1
                    WHERE classroom_id = ? AND version = ?
                    """,
                    UtcTimestamps.toText(updatedAtUtc),
                    classroomId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updatedRows, "Classroom was modified concurrently.");
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Classroom could not be archived.", exception);
        }
    }

    @Override
    public void authorizeApplication(String classroomId, String applicationId, OffsetDateTime createdAtUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO classroom_applications (classroom_id, application_id, created_at_utc)
                    VALUES (?, ?, ?)
                    ON CONFLICT(classroom_id, application_id) DO NOTHING
                    """,
                    classroomId,
                    applicationId,
                    UtcTimestamps.toText(createdAtUtc));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Classroom application could not be authorized.", exception);
        }
    }

    private Classroom classroomFromRow(
            String classroomId,
            String displayName,
            String defaultBrowserProfileId,
            boolean workspaceRecoveryPlanned,
            boolean batchConfirmationsRequired) {
        return new Classroom(
                classroomId,
                displayName,
                ids("SELECT device_id FROM devices WHERE classroom_id = ? ORDER BY display_name", classroomId),
                ids("SELECT student_id FROM students WHERE classroom_id = ? ORDER BY display_name", classroomId),
                ids("SELECT group_id FROM school_groups WHERE classroom_id = ? ORDER BY display_name", classroomId),
                new ClassroomConfiguration(
                        ids("SELECT application_id FROM classroom_applications WHERE classroom_id = ? ORDER BY application_id",
                                classroomId).stream().collect(java.util.stream.Collectors.toSet()),
                        defaultBrowserProfileId,
                        workspaceRecoveryPlanned,
                        batchConfirmationsRequired));
    }

    private List<String> ids(String sql, String classroomId) {
        return jdbcTemplate.query(sql, (rs, rowNum) -> rs.getString(1), classroomId);
    }
}
