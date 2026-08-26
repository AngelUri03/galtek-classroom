package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.application.ApplicationAvailability;
import com.galtek.classroom.application.ApplicationDefinition;
import com.galtek.classroom.application.ApplicationDefinitionRepository;
import com.galtek.classroom.application.ApplicationType;
import com.galtek.classroom.application.LaunchPolicy;
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
public class SqliteApplicationDefinitionRepository implements ApplicationDefinitionRepository {

    private static final RowMapper<ApplicationDefinition> ROW_MAPPER = (rs, rowNum) -> new ApplicationDefinition(
            rs.getString("application_id"),
            rs.getString("display_name"),
            ApplicationType.valueOf(rs.getString("type")),
            ApplicationAvailability.valueOf(rs.getString("availability")),
            LaunchPolicy.valueOf(rs.getString("launch_policy")));

    private final JdbcTemplate jdbcTemplate;

    public SqliteApplicationDefinitionRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(ApplicationDefinition application, OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO application_definitions (
                        application_id, display_name, type, availability, launch_policy,
                        active, created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, ?, ?, ?, 1, ?, ?, 0)
                    """,
                    application.applicationId(),
                    application.displayName(),
                    application.type().name(),
                    application.availability().name(),
                    application.launchPolicy().name(),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Application definition could not be created.", exception);
        }
    }

    @Override
    public Optional<ApplicationDefinition> findById(String applicationId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM application_definitions WHERE application_id = ?",
                ROW_MAPPER,
                applicationId);
    }

    @Override
    public List<ApplicationDefinition> findActive() {
        return jdbcTemplate.query(
                "SELECT * FROM application_definitions WHERE active = 1 ORDER BY display_name",
                ROW_MAPPER);
    }
}
