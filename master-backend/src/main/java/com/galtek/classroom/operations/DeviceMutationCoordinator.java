package com.galtek.classroom.operations;

import com.galtek.classroom.api.ApiException;
import java.time.Clock;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Objects;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.http.HttpStatus;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * Backend authority for exclusive, per-device mutations.
 *
 * <p>The lease is stored in SQLite so a browser reload, a second browser and a
 * Master process restart all observe the same guard. Batch acquisition is
 * sorted and committed in a single transaction: a busy target rejects the
 * whole batch before any new lease is written.</p>
 */
@Service
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled",
        havingValue = "true", matchIfMissing = true)
public class DeviceMutationCoordinator {
    public static final String IN_PROGRESS = "IN_PROGRESS";
    public static final String RECONCILIATION_REQUIRED = "RECONCILIATION_REQUIRED";

    private final JdbcTemplate jdbcTemplate;
    private final Clock clock;

    public DeviceMutationCoordinator(JdbcTemplate jdbcTemplate, Clock clock) {
        this.jdbcTemplate = jdbcTemplate;
        this.clock = clock;
    }

    @Transactional
    public synchronized Lease acquire(
            List<String> deviceIds,
            String operationType,
            String targetProfile) {
        List<String> targets = normalizedTargets(deviceIds);
        List<DeviceMutationState> occupied = find(targets);
        if (!occupied.isEmpty()) {
            throw busy(occupied.size());
        }

        String startedAtUtc = OffsetDateTime.ofInstant(clock.instant(), ZoneOffset.UTC).toString();
        try {
            for (String deviceId : targets) {
                jdbcTemplate.update("""
                        INSERT INTO device_mutation_leases (
                            device_id, state, operation_type, target_profile, started_at_utc
                        ) VALUES (?, ?, ?, ?, ?)
                        """, deviceId, IN_PROGRESS, required(operationType, "operationType"),
                        cleanOptional(targetProfile), startedAtUtc);
            }
        } catch (DataIntegrityViolationException exception) {
            throw busy(1);
        }
        return new Lease(targets);
    }

    @Transactional
    public void complete(Lease lease) {
        if (lease == null) return;
        for (String deviceId : lease.deviceIds()) {
            jdbcTemplate.update("DELETE FROM device_mutation_leases WHERE device_id = ?", deviceId);
        }
    }

    @Transactional
    public void requireReconciliation(Lease lease) {
        if (lease == null) return;
        for (String deviceId : lease.deviceIds()) {
            jdbcTemplate.update("""
                    UPDATE device_mutation_leases
                    SET state = ?
                    WHERE device_id = ?
                    """, RECONCILIATION_REQUIRED, deviceId);
        }
    }

    /** Clears only a reconciliation lease after a successful authoritative read. */
    @Transactional
    public void confirmStable(List<String> deviceIds, List<String> operationTypes) {
        List<String> types = normalizedTargets(operationTypes);
        if (types.isEmpty()) return;
        String placeholders = String.join(",", types.stream().map(ignored -> "?").toList());
        for (String deviceId : normalizedTargets(deviceIds)) {
            List<Object> arguments = new ArrayList<>();
            arguments.add(deviceId);
            arguments.add(RECONCILIATION_REQUIRED);
            arguments.addAll(types);
            jdbcTemplate.update("""
                    DELETE FROM device_mutation_leases
                    WHERE device_id = ? AND state = ? AND operation_type IN (%s)
                    """.formatted(placeholders), arguments.toArray());
        }
    }

    public List<DeviceMutationState> find(List<String> deviceIds) {
        List<String> targets = normalizedTargets(deviceIds);
        if (targets.isEmpty()) return List.of();
        String placeholders = String.join(",", targets.stream().map(ignored -> "?").toList());
        return jdbcTemplate.query("""
                        SELECT device_id, state, operation_type, target_profile, started_at_utc
                        FROM device_mutation_leases
                        WHERE device_id IN (%s)
                        ORDER BY device_id
                        """.formatted(placeholders),
                (resultSet, rowNumber) -> new DeviceMutationState(
                        resultSet.getString("device_id"),
                        resultSet.getString("state"),
                        resultSet.getString("operation_type"),
                        resultSet.getString("target_profile"),
                        resultSet.getString("started_at_utc")),
                targets.toArray());
    }

    private static List<String> normalizedTargets(List<String> deviceIds) {
        if (deviceIds == null) return List.of();
        List<String> targets = new ArrayList<>();
        for (String deviceId : deviceIds) {
            if (deviceId == null || deviceId.isBlank()) continue;
            String clean = deviceId.trim();
            if (!targets.contains(clean)) targets.add(clean);
        }
        targets.sort(Comparator.naturalOrder());
        return List.copyOf(targets);
    }

    private static String required(String value, String field) {
        String clean = Objects.requireNonNullElse(value, "").trim();
        if (clean.isEmpty()) throw new IllegalArgumentException(field + " is required.");
        return clean;
    }

    private static String cleanOptional(String value) {
        return value == null || value.isBlank() ? null : value.trim();
    }

    private static ApiException busy(int occupiedCount) {
        String message = occupiedCount == 1
                ? "One device has another operation in progress."
                : occupiedCount + " devices have another operation in progress.";
        return new ApiException(HttpStatus.CONFLICT, ErrorCode.DEVICE_OPERATION_IN_PROGRESS, message);
    }

    public record Lease(List<String> deviceIds) {
        public Lease {
            deviceIds = List.copyOf(deviceIds);
        }
    }

    public record DeviceMutationState(
            String deviceId,
            String state,
            String operationType,
            String targetProfile,
            String startedAtUtc) {
    }
}
