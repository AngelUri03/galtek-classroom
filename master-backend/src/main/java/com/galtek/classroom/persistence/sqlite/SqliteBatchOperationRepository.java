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
import com.galtek.classroom.operations.StoredBatchOperation;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.PersistenceVersionConflictException;
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
                (rs, rowNum) -> operationFromRow(rs.getString("operation_id"), rs),
                operationId);
    }

    @Override
    public Optional<StoredBatchOperation> findStoredById(String operationId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM batch_operations WHERE operation_id = ?",
                (rs, rowNum) -> new StoredBatchOperation(
                        rs.getString("classroom_id"),
                        operationFromRow(rs.getString("operation_id"), rs),
                        payloadFromRow(
                                rs.getObject("payload_schema_version"),
                                rs.getString("payload_json")),
                        rs.getLong("version")),
                operationId);
    }

    @Override
    public List<BatchTargetResult> findRetryableFailures(String operationId) {
        return jdbcTemplate.query("""
                SELECT btr.*, bo.operation_type
                FROM batch_target_results btr
                INNER JOIN batch_operations bo ON bo.operation_id = btr.operation_id
                WHERE btr.operation_id = ? AND btr.status = 'FAILED' AND btr.error_code IS NOT NULL
                ORDER BY btr.target_display_name
                """,
                (rs, rowNum) -> new RetryableTargetRow(
                        TARGET_MAPPER.mapRow(rs, rowNum),
                        OperationType.valueOf(rs.getString("operation_type"))),
                operationId).stream()
                .filter(row -> retryableFor(row.operationType(), row.target()))
                .map(RetryableTargetRow::target)
                .toList();
    }

    @Override
    @Transactional
    public StoredBatchOperation claimRetryTargets(
            String operationId,
            long expectedVersion,
            List<BatchTargetResult> expectedTargets,
            OffsetDateTime nowUtc) {
        try {
            int operationUpdated = jdbcTemplate.update("""
                    UPDATE batch_operations
                    SET completed_at_utc = NULL,
                        status = ?,
                        version = version + 1
                    WHERE operation_id = ? AND version = ?
                    """,
                    BatchOperationStatus.RUNNING.name(),
                    operationId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(operationUpdated, "Batch operation was modified concurrently.");

            for (BatchTargetResult expected : expectedTargets) {
                int targetUpdated = jdbcTemplate.update("""
                        UPDATE batch_target_results
                        SET status = ?,
                            error_code = NULL,
                            message = ?,
                            attempt = ?,
                            updated_at_utc = ?
                        WHERE operation_id = ?
                          AND target_type = ?
                          AND target_id = ?
                          AND status = ?
                          AND error_code = ?
                          AND attempt = ?
                        """,
                        TargetExecutionStatus.PENDING.name(),
                        "Retry dispatch pending.",
                        expected.attempt() + 1,
                        UtcTimestamps.toText(nowUtc),
                        operationId,
                        expected.target().type().name(),
                        expected.target().targetId(),
                        TargetExecutionStatus.FAILED.name(),
                        expected.errorCode().name(),
                        expected.attempt());
                SqliteJdbc.requireUpdated(targetUpdated, "Batch target was modified concurrently.");
            }

            return findStoredById(operationId).orElseThrow(
                    () -> new PersistenceVersionConflictException("Batch operation was not found."));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Batch operation retry targets could not be claimed.", exception);
        }
    }

    @Override
    @Transactional
    public StoredBatchOperation finishRetryTargets(
            String operationId,
            List<BatchTargetResult> finalTargets,
            OffsetDateTime nowUtc) {
        try {
            for (BatchTargetResult target : finalTargets) {
                int targetUpdated = jdbcTemplate.update("""
                        UPDATE batch_target_results
                        SET status = ?,
                            error_code = ?,
                            message = ?,
                            updated_at_utc = ?
                        WHERE operation_id = ?
                          AND target_type = ?
                          AND target_id = ?
                          AND status = ?
                          AND attempt = ?
                        """,
                        target.status().name(),
                        target.errorCode() == null ? null : target.errorCode().name(),
                        target.message(),
                        UtcTimestamps.toText(nowUtc),
                        operationId,
                        target.target().type().name(),
                        target.target().targetId(),
                        TargetExecutionStatus.PENDING.name(),
                        target.attempt());
                SqliteJdbc.requireUpdated(targetUpdated, "Batch target retry result was modified concurrently.");
            }

            StoredBatchOperation current = findStoredById(operationId).orElseThrow(
                    () -> new PersistenceVersionConflictException("Batch operation was not found."));
            BatchOperation recalculated = BatchOperation.fromTargets(
                    current.operation().operationId(),
                    current.operation().type(),
                    current.operation().requestedBy(),
                    current.operation().createdAtUtc(),
                    current.operation().targets());
            int operationUpdated = jdbcTemplate.update("""
                    UPDATE batch_operations
                    SET completed_at_utc = ?,
                        status = ?,
                        target_count = ?,
                        version = version + 1
                    WHERE operation_id = ?
                    """,
                    recalculated.status() == BatchOperationStatus.RUNNING
                            ? null
                            : UtcTimestamps.toText(nowUtc),
                    recalculated.status().name(),
                    recalculated.targetCount(),
                    operationId);
            SqliteJdbc.requireUpdated(operationUpdated, "Batch operation was not found.");
            return findStoredById(operationId).orElseThrow(
                    () -> new PersistenceVersionConflictException("Batch operation was not found."));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Batch operation retry targets could not be finished.", exception);
        }
    }

    @Override
    public List<BatchOperation> findPowerOperationsWithUnknownTarget(String deviceId) {
        return jdbcTemplate.query("""
                SELECT DISTINCT bo.*
                FROM batch_operations bo
                INNER JOIN batch_target_results btr ON btr.operation_id = bo.operation_id
                WHERE bo.operation_type IN ('SHUTDOWN', 'RESTART')
                  AND btr.target_type = 'DEVICE'
                  AND btr.target_id = ?
                  AND btr.status = 'FAILED'
                  AND btr.error_code = 'OPERATION_RESULT_UNKNOWN'
                ORDER BY bo.created_at_utc DESC
                """,
                (rs, rowNum) -> new BatchOperation(
                        rs.getString("operation_id"),
                        OperationType.valueOf(rs.getString("operation_type")),
                        rs.getString("requested_by"),
                        UtcTimestamps.fromText(rs.getString("created_at_utc")),
                        rs.getInt("target_count"),
                        BatchOperationStatus.valueOf(rs.getString("status")),
                        targetsFor(rs.getString("operation_id"))),
                deviceId);
    }

    @Override
    public List<BatchOperation> findPowerOperationsWithPendingTargetsCreatedBefore(OffsetDateTime recoveryCutoffUtc) {
        return jdbcTemplate.query("""
                SELECT DISTINCT bo.*
                FROM batch_operations bo
                INNER JOIN batch_target_results btr ON btr.operation_id = bo.operation_id
                WHERE bo.operation_type IN ('SHUTDOWN', 'RESTART')
                  AND bo.created_at_utc < ?
                  AND btr.target_type = 'DEVICE'
                  AND btr.status = 'PENDING'
                ORDER BY bo.created_at_utc
                """,
                (rs, rowNum) -> new BatchOperation(
                        rs.getString("operation_id"),
                        OperationType.valueOf(rs.getString("operation_type")),
                        rs.getString("requested_by"),
                        UtcTimestamps.fromText(rs.getString("created_at_utc")),
                        rs.getInt("target_count"),
                        BatchOperationStatus.valueOf(rs.getString("status")),
                        targetsFor(rs.getString("operation_id"))),
                UtcTimestamps.toText(recoveryCutoffUtc));
    }

    private List<BatchTargetResult> targetsFor(String operationId) {
        return jdbcTemplate.query(
                "SELECT * FROM batch_target_results WHERE operation_id = ? ORDER BY target_display_name",
                TARGET_MAPPER,
                operationId);
    }

    private BatchOperation operationFromRow(String operationId, java.sql.ResultSet rs) throws java.sql.SQLException {
        return new BatchOperation(
                operationId,
                OperationType.valueOf(rs.getString("operation_type")),
                rs.getString("requested_by"),
                UtcTimestamps.fromText(rs.getString("created_at_utc")),
                rs.getInt("target_count"),
                BatchOperationStatus.valueOf(rs.getString("status")),
                targetsFor(operationId));
    }

    private OperationPayload payloadFromRow(Object schemaVersion, String json) {
        if (schemaVersion == null) {
            return null;
        }
        return new OperationPayload(((Number) schemaVersion).intValue(), json);
    }

    private boolean retryableFor(OperationType operationType, BatchTargetResult target) {
        if (operationType == OperationType.SWITCH_MANAGED_ACCOUNT
                && target.errorCode() == ErrorCode.OPERATION_RESULT_UNKNOWN) {
            return false;
        }
        return target.retryable();
    }

    private record RetryableTargetRow(
            BatchTargetResult target,
            OperationType operationType) {
    }
}
