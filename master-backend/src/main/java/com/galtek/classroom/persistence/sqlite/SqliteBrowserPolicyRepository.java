package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.browserpolicy.BrowserAccessPolicy;
import com.galtek.classroom.browserpolicy.BrowserPolicyAccountScope;
import com.galtek.classroom.browserpolicy.BrowserPolicyMode;
import com.galtek.classroom.browserpolicy.BrowserPolicyRepository;
import com.galtek.classroom.browserpolicy.BrowserPolicyScopeType;
import com.galtek.classroom.browserpolicy.BrowserUrlMatchType;
import com.galtek.classroom.browserpolicy.BrowserUrlRule;
import com.galtek.classroom.browserpolicy.BrowserUrlRuleAction;
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
public class SqliteBrowserPolicyRepository implements BrowserPolicyRepository {

    private final JdbcTemplate jdbcTemplate;

    public SqliteBrowserPolicyRepository(JdbcTemplate jdbcTemplate) {
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
    public List<BrowserAccessPolicy> findPoliciesByClassroomId(String classroomId, Boolean active) {
        return query(() -> {
            List<Object> args = new ArrayList<>();
            args.add(classroomId);
            String sql = """
                    SELECT *
                    FROM browser_access_policies
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
    public Optional<BrowserAccessPolicy> findPolicyById(String policyId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM browser_access_policies WHERE policy_id = ?",
                (rs, rowNum) -> policyFromRow(rs),
                policyId));
    }

    @Override
    public void createPolicy(BrowserAccessPolicy policy) {
        execute(() -> jdbcTemplate.update("""
                INSERT INTO browser_access_policies (
                    policy_id, classroom_id, name, mode, scope_type, school_group_id,
                    device_id, account_scope, active, version, created_at_utc, updated_at_utc
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                policy.policyId(),
                policy.classroomId(),
                policy.name(),
                policy.mode().name(),
                policy.scopeType().name(),
                policy.schoolGroupId(),
                policy.deviceId(),
                policy.accountScope().name(),
                SqliteJdbc.bool(policy.active()),
                policy.version(),
                UtcTimestamps.toText(policy.createdAtUtc()),
                UtcTimestamps.toText(policy.updatedAtUtc())), "Browser policy could not be created.");
    }

    @Override
    public void updatePolicy(
            String policyId,
            String name,
            BrowserPolicyMode mode,
            BrowserPolicyScopeType scopeType,
            String schoolGroupId,
            String deviceId,
            BrowserPolicyAccountScope accountScope,
            long expectedVersion,
            OffsetDateTime updatedAtUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE browser_access_policies
                    SET name = ?,
                        mode = ?,
                        scope_type = ?,
                        school_group_id = ?,
                        device_id = ?,
                        account_scope = ?,
                        updated_at_utc = ?,
                        version = version + 1
                    WHERE policy_id = ? AND active = 1 AND version = ?
                    """,
                    name,
                    mode.name(),
                    scopeType.name(),
                    schoolGroupId,
                    deviceId,
                    accountScope.name(),
                    UtcTimestamps.toText(updatedAtUtc),
                    policyId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Browser policy was modified concurrently.");
        }, "Browser policy could not be updated.");
    }

    @Override
    public void archivePolicy(String policyId, long expectedVersion, OffsetDateTime updatedAtUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE browser_access_policies
                    SET active = 0, updated_at_utc = ?, version = version + 1
                    WHERE policy_id = ? AND active = 1 AND version = ?
                    """,
                    UtcTimestamps.toText(updatedAtUtc),
                    policyId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Browser policy was modified concurrently.");
        }, "Browser policy could not be archived.");
    }

    @Override
    public List<BrowserUrlRule> findRulesByPolicyId(String policyId, Boolean enabled) {
        return query(() -> {
            List<Object> args = new ArrayList<>();
            args.add(policyId);
            String sql = """
                    SELECT *
                    FROM browser_url_rules
                    WHERE policy_id = ?
                    """;
            if (enabled != null) {
                sql += " AND enabled = ?";
                args.add(SqliteJdbc.bool(enabled));
            }
            sql += " ORDER BY created_at_utc, rule_id";
            return jdbcTemplate.query(sql, (rs, rowNum) -> ruleFromRow(rs), args.toArray());
        });
    }

    @Override
    public Optional<BrowserUrlRule> findRuleById(String ruleId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM browser_url_rules WHERE rule_id = ?",
                (rs, rowNum) -> ruleFromRow(rs),
                ruleId));
    }

    @Override
    public void createRule(BrowserUrlRule rule) {
        execute(() -> jdbcTemplate.update("""
                INSERT INTO browser_url_rules (
                    rule_id, policy_id, action, match_type, pattern, enabled,
                    description, version, created_at_utc, updated_at_utc
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                rule.ruleId(),
                rule.policyId(),
                rule.action().name(),
                rule.matchType().name(),
                rule.pattern(),
                SqliteJdbc.bool(rule.enabled()),
                rule.description(),
                rule.version(),
                UtcTimestamps.toText(rule.createdAtUtc()),
                UtcTimestamps.toText(rule.updatedAtUtc())), "Browser URL rule could not be created.");
    }

    @Override
    public void updateRule(
            String ruleId,
            BrowserUrlRuleAction action,
            BrowserUrlMatchType matchType,
            String pattern,
            boolean enabled,
            String description,
            long expectedVersion,
            OffsetDateTime updatedAtUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE browser_url_rules
                    SET action = ?,
                        match_type = ?,
                        pattern = ?,
                        enabled = ?,
                        description = ?,
                        updated_at_utc = ?,
                        version = version + 1
                    WHERE rule_id = ? AND version = ?
                    """,
                    action.name(),
                    matchType.name(),
                    pattern,
                    SqliteJdbc.bool(enabled),
                    description,
                    UtcTimestamps.toText(updatedAtUtc),
                    ruleId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Browser URL rule was modified concurrently.");
        }, "Browser URL rule could not be updated.");
    }

    @Override
    public void archiveRule(String ruleId, long expectedVersion, OffsetDateTime updatedAtUtc) {
        execute(() -> {
            int updated = jdbcTemplate.update("""
                    UPDATE browser_url_rules
                    SET enabled = 0, updated_at_utc = ?, version = version + 1
                    WHERE rule_id = ? AND enabled = 1 AND version = ?
                    """,
                    UtcTimestamps.toText(updatedAtUtc),
                    ruleId,
                    expectedVersion);
            SqliteJdbc.requireUpdated(updated, "Browser URL rule was modified concurrently.");
        }, "Browser URL rule could not be archived.");
    }

    private int count(String sql, Object... args) {
        return query(() -> {
            Integer count = jdbcTemplate.queryForObject(sql, Integer.class, args);
            return count == null ? 0 : count;
        });
    }

    private BrowserAccessPolicy policyFromRow(ResultSet rs) throws SQLException {
        return new BrowserAccessPolicy(
                rs.getString("policy_id"),
                rs.getString("classroom_id"),
                rs.getString("name"),
                BrowserPolicyMode.valueOf(rs.getString("mode")),
                BrowserPolicyScopeType.valueOf(rs.getString("scope_type")),
                rs.getString("school_group_id"),
                rs.getString("device_id"),
                BrowserPolicyAccountScope.valueOf(rs.getString("account_scope")),
                SqliteJdbc.bool(rs, "active"),
                rs.getLong("version"),
                UtcTimestamps.fromText(rs.getString("created_at_utc")),
                UtcTimestamps.fromText(rs.getString("updated_at_utc")));
    }

    private BrowserUrlRule ruleFromRow(ResultSet rs) throws SQLException {
        return new BrowserUrlRule(
                rs.getString("rule_id"),
                rs.getString("policy_id"),
                BrowserUrlRuleAction.valueOf(rs.getString("action")),
                BrowserUrlMatchType.valueOf(rs.getString("match_type")),
                rs.getString("pattern"),
                SqliteJdbc.bool(rs, "enabled"),
                rs.getString("description"),
                rs.getLong("version"),
                UtcTimestamps.fromText(rs.getString("created_at_utc")),
                UtcTimestamps.fromText(rs.getString("updated_at_utc")));
    }

    private <T> T query(Supplier<T> query) {
        try {
            return query.get();
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Browser policy query failed.", exception);
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
