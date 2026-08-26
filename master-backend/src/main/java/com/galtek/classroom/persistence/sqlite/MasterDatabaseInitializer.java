package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageException;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.nio.file.Files;
import javax.sql.DataSource;
import org.flywaydb.core.Flyway;
import org.flywaydb.core.api.FlywayException;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Component;

@Component
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class MasterDatabaseInitializer implements ApplicationRunner {

    private final DataSource dataSource;
    private final JdbcTemplate jdbcTemplate;
    private final MasterDatabasePath databasePath;
    private final MasterStorageProperties properties;
    private final MasterStorageState storageState;

    public MasterDatabaseInitializer(
            DataSource dataSource,
            JdbcTemplate jdbcTemplate,
            MasterDatabasePath databasePath,
            MasterStorageProperties properties,
            MasterStorageState storageState) {
        this.dataSource = dataSource;
        this.jdbcTemplate = jdbcTemplate;
        this.databasePath = databasePath;
        this.properties = properties;
        this.storageState = storageState;
    }

    @Override
    public void run(ApplicationArguments args) {
        boolean existingDatabase = Files.exists(databasePath.databaseFile())
                && databasePath.databaseFile().toFile().length() > 0;

        if (existingDatabase && properties.isQuickCheckOnStartup()) {
            quickCheck();
        }

        migrate();

        if (properties.isQuickCheckOnStartup()) {
            quickCheck();
        }

        storageState.markReady();
    }

    private void migrate() {
        try {
            Flyway.configure()
                    .dataSource(dataSource)
                    .locations("classpath:db/migration/sqlite")
                    .cleanDisabled(true)
                    .load()
                    .migrate();
        } catch (FlywayException exception) {
            storageState.mark(
                    MasterStorageStatus.MIGRATION_FAILED,
                    ErrorCode.MASTER_DATABASE_MIGRATION_FAILED.name());
            throw new MasterStorageException(
                    ErrorCode.MASTER_DATABASE_MIGRATION_FAILED,
                    "Master SQLite migrations failed.",
                    exception);
        }
    }

    private void quickCheck() {
        try {
            String result = jdbcTemplate.queryForObject("PRAGMA quick_check", String.class);
            if (!"ok".equalsIgnoreCase(result)) {
                storageState.mark(MasterStorageStatus.CORRUPT, ErrorCode.MASTER_DATABASE_CORRUPT.name());
                throw new MasterStorageException(
                        ErrorCode.MASTER_DATABASE_CORRUPT,
                        "Master SQLite quick_check did not return ok.");
            }
        } catch (DataAccessException exception) {
            MasterStorageException mapped = SqliteExceptionMapper.map(
                    "Master SQLite quick_check failed.",
                    exception);
            storageState.mark(statusFor(mapped.errorCode()), mapped.errorCode().name());
            throw mapped;
        }
    }

    private MasterStorageStatus statusFor(ErrorCode errorCode) {
        if (errorCode == ErrorCode.MASTER_DATABASE_CORRUPT) {
            return MasterStorageStatus.CORRUPT;
        }
        if (errorCode == ErrorCode.MASTER_DATABASE_MIGRATION_FAILED) {
            return MasterStorageStatus.MIGRATION_FAILED;
        }
        return MasterStorageStatus.UNAVAILABLE;
    }
}
