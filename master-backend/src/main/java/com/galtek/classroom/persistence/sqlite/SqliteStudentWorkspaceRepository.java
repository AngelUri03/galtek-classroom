package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.workspace.LogicalWorkspaceDestination;
import com.galtek.classroom.workspace.StudentWorkspace;
import com.galtek.classroom.workspace.StudentWorkspaceRepository;
import com.galtek.classroom.workspace.WorkspaceRecoveryPolicy;
import com.galtek.classroom.workspace.WorkspaceStatus;
import java.time.OffsetDateTime;
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
public class SqliteStudentWorkspaceRepository implements StudentWorkspaceRepository {

    private static final RowMapper<StudentWorkspace> ROW_MAPPER = (rs, rowNum) -> new StudentWorkspace(
            rs.getString("workspace_id"),
            rs.getString("student_id"),
            WorkspaceStatus.valueOf(rs.getString("status")),
            JsonText.enumSet(rs.getString("allowed_destinations_json"), LogicalWorkspaceDestination.class),
            rs.getString("browser_profile_id"),
            new WorkspaceRecoveryPolicy(
                    SqliteJdbc.bool(rs, "recovery_lightweight_history_planned"),
                    SqliteJdbc.bool(rs, "recovery_controlled_trash_planned"),
                    SqliteJdbc.bool(rs, "recovery_limited_snapshots_planned"),
                    SqliteJdbc.bool(rs, "recovery_restore_last_valid_state_planned"),
                    SqliteJdbc.bool(rs, "recovery_recover_teacher_distributed_files_planned")));

    private final JdbcTemplate jdbcTemplate;

    public SqliteStudentWorkspaceRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(StudentWorkspace workspace, OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO student_workspaces (
                        workspace_id, student_id, status, allowed_destinations_json, browser_profile_id,
                        recovery_lightweight_history_planned, recovery_controlled_trash_planned,
                        recovery_limited_snapshots_planned, recovery_restore_last_valid_state_planned,
                        recovery_recover_teacher_distributed_files_planned,
                        created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)
                    """,
                    workspace.workspaceId(),
                    workspace.studentId(),
                    workspace.status().name(),
                    JsonText.enumNames(workspace.allowedDestinations()),
                    workspace.browserProfileId(),
                    SqliteJdbc.bool(workspace.recoveryPolicy().lightweightHistoryPlanned()),
                    SqliteJdbc.bool(workspace.recoveryPolicy().controlledTrashPlanned()),
                    SqliteJdbc.bool(workspace.recoveryPolicy().limitedSnapshotsPlanned()),
                    SqliteJdbc.bool(workspace.recoveryPolicy().restoreLastValidStatePlanned()),
                    SqliteJdbc.bool(workspace.recoveryPolicy().recoverTeacherDistributedFilesPlanned()),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Student workspace could not be created.", exception);
        }
    }

    @Override
    public Optional<StudentWorkspace> findById(String workspaceId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM student_workspaces WHERE workspace_id = ?",
                ROW_MAPPER,
                workspaceId);
    }

    @Override
    public Optional<StudentWorkspace> findByStudentId(String studentId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM student_workspaces WHERE student_id = ?",
                ROW_MAPPER,
                studentId);
    }
}
