package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.browser.BrowserProfile;
import com.galtek.classroom.browser.BrowserProfilePortability;
import com.galtek.classroom.browser.BrowserProfileRepository;
import com.galtek.classroom.browser.BrowserProfileStatus;
import com.galtek.classroom.browser.BrowserProfileStrategy;
import com.galtek.classroom.browser.BrowserType;
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
public class SqliteBrowserProfileRepository implements BrowserProfileRepository {

    private static final RowMapper<BrowserProfile> ROW_MAPPER = (rs, rowNum) -> new BrowserProfile(
            rs.getString("browser_profile_id"),
            rs.getString("student_id"),
            BrowserType.valueOf(rs.getString("browser_type")),
            rs.getString("display_name"),
            BrowserProfileStrategy.valueOf(rs.getString("profile_strategy")),
            rs.getString("profile_reference"),
            BrowserProfileStatus.valueOf(rs.getString("status")),
            BrowserProfilePortability.valueOf(rs.getString("portability")));

    private final JdbcTemplate jdbcTemplate;

    public SqliteBrowserProfileRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(BrowserProfile profile, OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO browser_profiles (
                        browser_profile_id, student_id, browser_type, display_name, profile_strategy,
                        profile_reference, status, portability, active, created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, 0)
                    """,
                    profile.browserProfileId(),
                    profile.studentId(),
                    profile.browserType().name(),
                    profile.displayName(),
                    profile.profileStrategy().name(),
                    profile.profileReference(),
                    profile.status().name(),
                    profile.portability().name(),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Browser profile could not be created.", exception);
        }
    }

    @Override
    public Optional<BrowserProfile> findById(String browserProfileId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM browser_profiles WHERE browser_profile_id = ?",
                ROW_MAPPER,
                browserProfileId);
    }

    @Override
    public Optional<BrowserProfile> findByStudentId(String studentId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM browser_profiles WHERE student_id = ?",
                ROW_MAPPER,
                studentId);
    }
}
