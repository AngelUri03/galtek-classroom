package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchOperationRepository;
import com.galtek.classroom.operations.BatchOperationStatus;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationPayload;
import com.galtek.classroom.operations.OperationTarget;
import com.galtek.classroom.operations.OperationTargetType;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.core.RowMapper;
import org.springframework.stereotype.Repository;
import org.springframework.transaction.annotation.Transactional;

@Repository
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class SqliteBatchOperationRepository implements BatchOperationRepository {

    private static final RowMapper<BatchTargetResult> TARGET_MAPPER = (rs, rowNum) -> {
        String errorCode = rs.getString("error_code");
        return new BatchTargetResult(
                new OperationTarget(
                        OperationTargetType.valueOf(rs.getString("target_type")),
                        rs.getString("target_id"),
                        rs.getString("target_display_name")),
                TargetExecutionStatus.valueOf(rs.getString("status")),
                errorCode == null ? null : ErrorCode.valueOf(errorCode),
                rs.getString("message"),
                rs.getInt("attempt"));
    };

    private final JdbcTemplate jdbcTemplate;

    public SqliteBatchOperationRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    @Transactional
    public void create(
            String classroomId,
            BatchOperation operation,
            OperationPayload payload,
            OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO batch_operations (
                        operation_id, classroom_id, operation_type, requested_by, created_at_utc,
                        started_at_utc, completed_at_utc, status, target_count,
                        payload_schema_version, payload_json, version
                    ) VALUES (?, ?, ?, ?, ?, NULL, NULL, ?, ?, ?, ?, 0)
                    """,
                    operation.operationId(),
                    classroomId,
                    operation.type().name(),
                    operation.requestedBy(),
                    UtcTimestamps.toText(operation.createdAtUtc()),
                    operation.status().name(),
                    operation.targetCount(),
                    payload == null ? null : payload.schemaVersion(),
                    payload == null ? null : payload.json());

            for (BatchTargetResult target : operation.targets()) {
                jdbcTemplate.update("""
                        INSERT INTO batch_target_results (
                            operation_id, target_type, target_id, target_display_name,
                            status, error_code, message, attempt, updated_at_utc
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """,
                        operation.operationId(),
                        target.target().type().name(),
                        target.target().targetId(),
                        target.target().displayName(),
                        target.status().name(),
                        target.errorCode() == null ? null : target.errorCode().name(),
                        target.message(),
                        target.attempt(),
                        UtcTimestamps.toText(nowUtc));
            }
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Batch operation could not be created.", exception);
        }
    }

    @Override
    @Transactional
    public void replaceResults(BatchOperation operation, OffsetDateTime nowUtc) {
        try {
            int updated = jdbcTemplate.update("""
                    UPDATE batch_operations
                    SET completed_at_utc = ?,
                        status = ?,
                        target_count = ?,
                        version = version + 1
                    WHERE operation_id = ?
                    """,
                    UtcTimestamps.toText(nowUtc),
                    operation.status().name(),
                    operation.targetCount(),
                    operation.operationId());
            SqliteJdbc.requireUpdated(updated, "Batch operation was not found.");

            jdbcTemplate.update(
                    "DELETE FROM batch_target_results WHERE operation_id = ?",
                    operation.operationId());

            for (BatchTargetResult target : operation.targets()) {
                jdbcTemplate.update("""
                        INSERT INTO batch_target_results (
                            operation_id, target_type, target_id, target_display_name,
                            status, error_code, message, attempt, updated_at_utc
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """,
                        operation.operationId(),
                        target.target().type().name(),
                        target.target().targetId(),
                        target.target().displayName(),
                        target.status().name(),
                        target.errorCode() == null ? null : target.errorCode().name(),
                        target.message(),
                        target.attempt(),
                        UtcTimestamps.toText(nowUtc));
            }
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Batch operation results could not be replaced.", exception);
        }
    }

    @Override
    public Optional<BatchOperation> findById(String operationId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM batch_operations WHERE operation_id = ?",
                (rs, rowNum) -> new BatchOperation(
                        rs.getString("operation_id"),
                        OperationType.valueOf(rs.getString("operation_type")),
                        rs.getString("requested_by"),
                        UtcTimestamps.fromText(rs.getString("created_at_utc")),
                        rs.getInt("target_count"),
                        BatchOperationStatus.valueOf(rs.getString("status")),
                        targetsFor(rs.getString("operation_id"))),
                operationId);
    }

    @Override
    public List<BatchTargetResult> findRetryableFailures(String operationId) {
        return jdbcTemplate.query("""
                SELECT *
                FROM batch_target_results
                WHERE operation_id = ? AND status = 'FAILED' AND error_code IS NOT NULL
                ORDER BY target_display_name
                """,
                TARGET_MAPPER,
                operationId).stream()
                .filter(BatchTargetResult::retryable)
                .toList();
    }

    private List<BatchTargetResult> targetsFor(String operationId) {
        return jdbcTemplate.query(
                "SELECT * FROM batch_target_results WHERE operation_id = ? ORDER BY target_display_name",
                TARGET_MAPPER,
                operationId);
    }
}
