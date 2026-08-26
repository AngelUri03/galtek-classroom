package com.galtek.classroom.persistence.sqlite;

import com.zaxxer.hikari.HikariConfig;
import com.zaxxer.hikari.HikariDataSource;
import java.io.IOException;
import java.nio.file.Files;
import java.time.Clock;
import javax.sql.DataSource;
import org.sqlite.SQLiteConfig;
import org.sqlite.SQLiteDataSource;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
@EnableConfigurationProperties(MasterStorageProperties.class)
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class MasterDatabaseConfiguration {

    @Bean
    CommonApplicationDataResolver commonApplicationDataResolver() {
        return new WindowsCommonApplicationDataResolver();
    }

    @Bean
    MasterDataDirectoryResolver masterDataDirectoryResolver(
            CommonApplicationDataResolver commonApplicationDataResolver) {
        return new MasterDataDirectoryResolver(commonApplicationDataResolver);
    }

    @Bean
    MasterDatabasePath masterDatabasePath(
            MasterStorageProperties properties,
            MasterDataDirectoryResolver dataDirectoryResolver) throws IOException {
        PathSafety.requireSimpleFileName(properties.getDatabaseFileName());
        var dataDirectory = dataDirectoryResolver.resolve(properties.getDataDir());
        Files.createDirectories(dataDirectory);
        return new MasterDatabasePath(dataDirectory, dataDirectory.resolve(properties.getDatabaseFileName()));
    }

    @Bean
    DataSource dataSource(MasterStorageProperties properties, MasterDatabasePath databasePath) {
        SQLiteConfig sqliteConfig = new SQLiteConfig();
        sqliteConfig.enforceForeignKeys(true);
        sqliteConfig.setJournalMode(SQLiteConfig.JournalMode.WAL);
        sqliteConfig.setSynchronous(SQLiteConfig.SynchronousMode.NORMAL);
        sqliteConfig.setBusyTimeout(Math.max(1, properties.getBusyTimeoutMs()));

        SQLiteDataSource sqliteDataSource = new SQLiteDataSource(sqliteConfig);
        sqliteDataSource.setUrl("jdbc:sqlite:" + databasePath.databaseFile().toUri().toASCIIString());

        HikariConfig hikariConfig = new HikariConfig();
        hikariConfig.setDataSource(sqliteDataSource);
        hikariConfig.setPoolName("galtek-classroom-master-sqlite");
        hikariConfig.setMaximumPoolSize(Math.max(1, properties.getMaximumPoolSize()));
        hikariConfig.setMinimumIdle(0);
        hikariConfig.setInitializationFailTimeout(-1);
        hikariConfig.setConnectionTestQuery("SELECT 1");

        return new HikariDataSource(hikariConfig);
    }

    @Bean
    Clock clock() {
        return Clock.systemUTC();
    }
}
