package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.browserpolicy.BrowserDownloadPolicy;
import com.galtek.classroom.browserpolicy.BrowserDownloadPolicyRepository;
import com.galtek.classroom.browserpolicy.BrowserDownloadRestrictionMode;
import com.galtek.classroom.browserpolicy.BrowserPolicyAccountScope;
import com.galtek.classroom.browserpolicy.BrowserPolicyScopeType;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.function.Supplier;
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
public class SqliteBrowserDownloadPolicyRepository implements BrowserDownloadPolicyRepository {

    private final JdbcTemplate jdbcTemplate;

    public SqliteBrowserDownloadPolicyRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public boolean classroomExists(String classroomId) {
        return count("""
                SELECT COUNT(*)
                FROM classrooms
                WHERE classroom_id = ?
                """, classroomId) > 0;
    }

    @Override
    public boolean groupBelongsToClassroom(String groupId, String classroomId) {
        return count("""
                SELECT COUNT(*)
                FROM school_groups
                WHERE group_id = ? AND classroom_id = ?
                """, groupId, classroomId) > 0;
    }

    @Override
    public boolean deviceBelongsToClassroom(String deviceId, String classroomId) {
        return count("""
                SELECT COUNT(*)
                FROM devices
                WHERE device_id = ? AND classroom_id = ?
                """, deviceId, classroomId) > 0;
    }

    @Override
    public List<BrowserDownloadPolicy> findPoliciesByClassroomId(String classroomId, Boolean active) {
        return query(() -> {
            List<Object> args = new ArrayList<>();
            args.add(classroomId);
            String sql = """
                    SELECT *
                    FROM browser_download_policies
                    WHERE classroom_id = ?
                    """;
            if (active != null) {
                sql += " AND active = ?";
                args.add(SqliteJdbc.bool(active));
            }
            sql += " ORDER BY scope_type, account_scope, name";
            return jdbcTemplate.query(sql, (rs, rowNum) -> policyFromRow(rs), args.toArray());
        });
    }

    @Override
    public Optional<BrowserDownloadPolicy> findPolicyById(String policyId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM browser_download_policies WHERE policy_id = ?",
                (rs, rowNum) -> policyFromRow(rs),
                policyId));
    }

    @Override
    public void createPolicy(BrowserDownloadPolicy policy) {
        execute(() -> jdbcTemplate.update("""
                INSERT INTO browser_download_policies (
                    policy_id, classroom_id, name, restriction_mode, scope_type, school_group_id,
                    device_id, account_scope, active, version, created_at_utc, updated_at_utc
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                policy.policyId(),
                policy.classroomId(),
                policy.name(),
                policy.restrictionMode().name(),
                policy.scopeType().name(),
                policy.schoolGroupId(),
                policy.deviceId(),
                policy.accountScope().name(),
                SqliteJdbc.bool(policy.active()),
                policy.version(),
                UtcTimestamps.toText(policy.createdAtUtc()),
                UtcTimestamps.toText(policy.updatedAtUtc())), "Browser download policy could not be created.");
    }

    @Override
    public void updatePolicy(
            String policyId,
            String name,
            BrowserDownloadRestrictionMode restrictionMode,
            BrowserPolicyScopeType scopeType,
            String schoolGroupId,
            String deviceId,
            BrowserPolicyAccountScope accountScope,
            long expectedVersion,
            OffsetDateTime updatedAtUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE browser_download_policies
                    SET name = ?,
                        restriction_mode = ?,
                        scope_type = ?,
                        school_group_id = ?,
                        device_id = ?,
                        account_scope = ?,
                        updated_at_utc = ?,
                        version = version + 1
                    WHERE policy_id = ? AND active = 1 AND version = ?
                    """,
                    name,
                    restrictionMode.name(),
                    scopeType.name(),
                    schoolGroupId,
                    deviceId,
                    accountScope.name(),
                    UtcTimestamps.toText(updatedAtUtc),
                    policyId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Browser download policy was modified concurrently.");
        }, "Browser download policy could not be updated.");
    }

    @Override
    public void archivePolicy(String policyId, long expectedVersion, OffsetDateTime updatedAtUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE browser_download_policies
                    SET active = 0, updated_at_utc = ?, version = version + 1
                    WHERE policy_id = ? AND active = 1 AND version = ?
                    """,
                    UtcTimestamps.toText(updatedAtUtc),
                    policyId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Browser download policy was modified concurrently.");
        }, "Browser download policy could not be archived.");
    }

    private int count(String sql, Object... args) {
        return query(() -> {
            Integer count = jdbcTemplate.queryForObject(sql, Integer.class, args);
            return count == null ? 0 : count;
        });
    }

    private BrowserDownloadPolicy policyFromRow(ResultSet rs) throws SQLException {
        return new BrowserDownloadPolicy(
                rs.getString("policy_id"),
                rs.getString("classroom_id"),
                rs.getString("name"),
                BrowserDownloadRestrictionMode.valueOf(rs.getString("restriction_mode")),
                BrowserPolicyScopeType.valueOf(rs.getString("scope_type")),
                rs.getString("school_group_id"),
                rs.getString("device_id"),
                BrowserPolicyAccountScope.valueOf(rs.getString("account_scope")),
                SqliteJdbc.bool(rs, "active"),
                rs.getLong("version"),
                UtcTimestamps.fromText(rs.getString("created_at_utc")),
                UtcTimestamps.fromText(rs.getString("updated_at_utc")));
    }

    private <T> T query(Supplier<T> query) {
        try {
            return query.get();
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Browser download policy query failed.", exception);
        }
    }

    private void execute(Runnable runnable, String message) {
        try {
            runnable.run();
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map(message, exception);
        }
    }
}
