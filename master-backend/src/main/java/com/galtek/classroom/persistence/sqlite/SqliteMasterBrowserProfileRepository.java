package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.browser.BrowserProfileStatus;
import com.galtek.classroom.browser.BrowserProfileStrategy;
import com.galtek.classroom.browser.BrowserType;
import com.galtek.classroom.browser.MasterBrowserProfile;
import com.galtek.classroom.browser.MasterBrowserProfileRepository;
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
public class SqliteMasterBrowserProfileRepository implements MasterBrowserProfileRepository {

    private static final RowMapper<MasterBrowserProfile> ROW_MAPPER = (rs, rowNum) -> new MasterBrowserProfile(
            rs.getString("master_browser_profile_id"),
            BrowserType.valueOf(rs.getString("browser_type")),
            rs.getString("display_name"),
            rs.getString("profile_reference"),
            BrowserProfileStatus.valueOf(rs.getString("status")));

    private final JdbcTemplate jdbcTemplate;

    public SqliteMasterBrowserProfileRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(
            MasterBrowserProfile profile,
            BrowserProfileStrategy strategy,
            String ownerWindowsSid,
            boolean defaultProfile,
            OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO master_browser_profiles (
                        master_browser_profile_id, browser_type, display_name, profile_strategy,
                        profile_reference, owner_windows_sid, status, active, default_profile,
                        created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, 0)
                    """,
                    profile.browserProfileId(),
                    profile.browserType().name(),
                    profile.displayName(),
                    strategy.name(),
                    profile.profileReference(),
                    ownerWindowsSid,
                    profile.status().name(),
                    SqliteJdbc.bool(defaultProfile),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Master browser profile could not be created.", exception);
        }
    }

    @Override
    public Optional<MasterBrowserProfile> findById(String browserProfileId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM master_browser_profiles WHERE master_browser_profile_id = ?",
                ROW_MAPPER,
                browserProfileId);
    }

    @Override
    public List<MasterBrowserProfile> findActiveByBrowserType(BrowserType browserType) {
        return jdbcTemplate.query(
                "SELECT * FROM master_browser_profiles WHERE browser_type = ? AND active = 1 ORDER BY default_profile DESC, display_name",
                ROW_MAPPER,
                browserType.name());
    }
}
