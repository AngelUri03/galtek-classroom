package com.galtek.classroom.persistence.sqlite;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.catchThrowable;

import com.galtek.classroom.MasterBackendApplication;
import com.galtek.classroom.browserpolicy.BrowserAccessPolicy;
import com.galtek.classroom.browserpolicy.BrowserDownloadPolicy;
import com.galtek.classroom.browserpolicy.BrowserDownloadPolicyRepository;
import com.galtek.classroom.browserpolicy.BrowserDownloadRestrictionMode;
import com.galtek.classroom.browserpolicy.BrowserPolicyAccountScope;
import com.galtek.classroom.browserpolicy.BrowserPolicyMode;
import com.galtek.classroom.browserpolicy.BrowserPolicyRepository;
import com.galtek.classroom.browserpolicy.BrowserPolicyScopeType;
import com.galtek.classroom.classroom.Classroom;
import com.galtek.classroom.classroom.ClassroomConfiguration;
import com.galtek.classroom.classroom.ClassroomManagementService;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceManagementService;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageException;
import com.galtek.classroom.persistence.PersistenceVersionConflictException;
import com.galtek.classroom.student.SchoolGroup;
import com.galtek.classroom.student.StudentManagementService;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.OffsetDateTime;
import java.util.EnumSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import org.flywaydb.core.Flyway;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.builder.SpringApplicationBuilder;
import org.springframework.context.ConfigurableApplicationContext;
import org.springframework.jdbc.core.JdbcTemplate;
import org.sqlite.SQLiteDataSource;

class BrowserDownloadPolicyPersistenceIntegrationTest {

    @TempDir
    private Path tempDir;

    @Test
    void v4CreatesDownloadPolicyTableIndexesAndNoExtensionModel() {
        Path dataDir = tempDir.resolve("schema");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            JdbcTemplate jdbcTemplate = context.getBean(JdbcTemplate.class);
            List<String> tables = jdbcTemplate.queryForList(
                    "SELECT name FROM sqlite_master WHERE type = 'table'",
                    String.class);
            assertThat(tables).contains("browser_download_policies");

            List<String> indexes = jdbcTemplate.queryForList(
                    "SELECT name FROM sqlite_master WHERE type = 'index'",
                    String.class);
            assertThat(indexes)
                    .contains(
                            "uq_browser_download_policy_active_classroom_account",
                            "uq_browser_download_policy_active_group_account",
                            "uq_browser_download_policy_active_device_account",
                            "ix_browser_download_policies_classroom",
                            "ix_browser_download_policies_group",
                            "ix_browser_download_policies_device");

            List<String> columns = jdbcTemplate.queryForList(
                    "SELECT name FROM pragma_table_info('browser_download_policies')",
                    String.class);
            assertThat(columns)
                    .contains(
                            "policy_id",
                            "classroom_id",
                            "name",
                            "restriction_mode",
                            "scope_type",
                            "school_group_id",
                            "device_id",
                            "account_scope",
                            "active",
                            "version",
                            "created_at_utc",
                            "updated_at_utc")
                    .noneMatch(column -> column.equalsIgnoreCase("blockedExtensions"))
                    .noneMatch(column -> column.equalsIgnoreCase("allowedExtensions"))
                    .noneMatch(column -> column.equalsIgnoreCase("blockedMimeTypes"))
                    .noneMatch(column -> column.equalsIgnoreCase("allowedMimeTypes"))
                    .noneMatch(column -> column.toLowerCase().contains("extension"))
                    .noneMatch(column -> column.toLowerCase().contains("mime"));
        }
    }

    @Test
    void existingV3DatabaseMigratesToV4WithoutLosingNavigationPolicies() throws Exception {
        Path dataDir = tempDir.resolve("v3-to-v4");
        Files.createDirectories(dataDir);
        Path database = dataDir.resolve("classroom.db");
        migrateToV3(database);

        String classroomId = id();
        String navigationPolicyId = id();
        JdbcTemplate v3Jdbc = new JdbcTemplate(sqliteDataSource(database));
        v3Jdbc.update("""
                INSERT INTO classrooms (
                    classroom_id, display_name, active, default_browser_profile_id,
                    workspace_recovery_planned, batch_confirmations_required,
                    created_at_utc, updated_at_utc, version
                ) VALUES (?, 'Aula V3', 1, NULL, 1, 1, ?, ?, 0)
                """,
                classroomId,
                nowText(),
                nowText());
        v3Jdbc.update("""
                INSERT INTO browser_access_policies (
                    policy_id, classroom_id, name, mode, scope_type, school_group_id,
                    device_id, account_scope, active, version, created_at_utc, updated_at_utc
                ) VALUES (?, ?, 'Navigation primary', 'ALLOWLIST', 'CLASSROOM', NULL, NULL,
                    'PRIMARY', 1, 0, ?, ?)
                """,
                navigationPolicyId,
                classroomId,
                nowText(),
                nowText());

        try (ConfigurableApplicationContext context = start(dataDir)) {
            assertThat(flywaySuccessCount(context)).isEqualTo(4);
            BrowserPolicyRepository navigationRepository = context.getBean(BrowserPolicyRepository.class);
            assertThat(navigationRepository.findPolicyById(navigationPolicyId))
                    .get()
                    .satisfies(policy -> {
                        assertThat(policy.classroomId()).isEqualTo(classroomId);
                        assertThat(policy.mode()).isEqualTo(BrowserPolicyMode.ALLOWLIST);
                    });
            assertThat(context.getBean(JdbcTemplate.class).queryForList(
                    "SELECT name FROM sqlite_master WHERE type = 'table'",
                    String.class))
                    .contains("browser_download_policies");
        }
    }

    @Test
    void reopenConservesDownloadPolicies() {
        Path dataDir = tempDir.resolve("reopen");
        String policyId = id();

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            createPolicy(
                    context.getBean(BrowserDownloadPolicyRepository.class),
                    policyId,
                    fixture.classroomId,
                    BrowserDownloadRestrictionMode.BLOCK_ALL,
                    BrowserPolicyScopeType.CLASSROOM,
                    null,
                    null,
                    BrowserPolicyAccountScope.PRIMARY);
        }

        try (ConfigurableApplicationContext reopened = start(dataDir)) {
            BrowserDownloadPolicyRepository repository = reopened.getBean(BrowserDownloadPolicyRepository.class);
            assertThat(repository.findPolicyById(policyId))
                    .get()
                    .satisfies(policy -> {
                        assertThat(policy.restrictionMode()).isEqualTo(BrowserDownloadRestrictionMode.BLOCK_ALL);
                        assertThat(policy.accountScope()).isEqualTo(BrowserPolicyAccountScope.PRIMARY);
                    });
        }
    }

    @Test
    void duplicateActiveSameTargetAccountFailsButPrimaryAndSecondaryCoexist() {
        Path dataDir = tempDir.resolve("uniqueness");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            BrowserDownloadPolicyRepository repository = context.getBean(BrowserDownloadPolicyRepository.class);
            createPolicy(
                    repository,
                    id(),
                    fixture.classroomId,
                    BrowserDownloadRestrictionMode.BLOCK_ALL,
                    BrowserPolicyScopeType.CLASSROOM,
                    null,
                    null,
                    BrowserPolicyAccountScope.PRIMARY);
            createPolicy(
                    repository,
                    id(),
                    fixture.classroomId,
                    BrowserDownloadRestrictionMode.BLOCK_MALICIOUS,
                    BrowserPolicyScopeType.CLASSROOM,
                    null,
                    null,
                    BrowserPolicyAccountScope.SECONDARY);

            Throwable duplicate = catchThrowable(() -> createPolicy(
                    repository,
                    id(),
                    fixture.classroomId,
                    BrowserDownloadRestrictionMode.BLOCK_DANGEROUS,
                    BrowserPolicyScopeType.CLASSROOM,
                    null,
                    null,
                    BrowserPolicyAccountScope.PRIMARY));

            assertThat(duplicate).isInstanceOf(MasterStorageException.class);
            assertThat(((MasterStorageException) duplicate).errorCode())
                    .isEqualTo(ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION);
        }
    }

    @Test
    void groupOrDeviceFromDifferentClassroomFails() {
        Path dataDir = tempDir.resolve("cross-classroom");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture classroomA = createFixture(context);
            Fixture classroomB = createFixture(context);
            BrowserDownloadPolicyRepository repository = context.getBean(BrowserDownloadPolicyRepository.class);

            Throwable wrongGroup = catchThrowable(() -> createPolicy(
                    repository,
                    id(),
                    classroomA.classroomId,
                    BrowserDownloadRestrictionMode.BLOCK_ALL,
                    BrowserPolicyScopeType.GROUP,
                    classroomB.groupId,
                    null,
                    BrowserPolicyAccountScope.ANY));
            Throwable wrongDevice = catchThrowable(() -> createPolicy(
                    repository,
                    id(),
                    classroomA.classroomId,
                    BrowserDownloadRestrictionMode.BLOCK_ALL,
                    BrowserPolicyScopeType.DEVICE,
                    null,
                    classroomB.deviceId,
                    BrowserPolicyAccountScope.ANY));

            assertThat(wrongGroup).isInstanceOf(MasterStorageException.class);
            assertThat(((MasterStorageException) wrongGroup).errorCode())
                    .isEqualTo(ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION);
            assertThat(wrongDevice).isInstanceOf(MasterStorageException.class);
            assertThat(((MasterStorageException) wrongDevice).errorCode())
                    .isEqualTo(ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION);
        }
    }

    @Test
    void archiveAllowsReplacementAndOptimisticVersionWorks() {
        Path dataDir = tempDir.resolve("archive-version");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            BrowserDownloadPolicyRepository repository = context.getBean(BrowserDownloadPolicyRepository.class);
            String policyId = id();
            createPolicy(
                    repository,
                    policyId,
                    fixture.classroomId,
                    BrowserDownloadRestrictionMode.BLOCK_ALL,
                    BrowserPolicyScopeType.CLASSROOM,
                    null,
                    null,
                    BrowserPolicyAccountScope.ANY);

            repository.updatePolicy(
                    policyId,
                    "Updated",
                    BrowserDownloadRestrictionMode.BLOCK_DANGEROUS,
                    BrowserPolicyScopeType.CLASSROOM,
                    null,
                    null,
                    BrowserPolicyAccountScope.ANY,
                    0,
                    now());
            Throwable staleUpdate = catchThrowable(() -> repository.updatePolicy(
                    policyId,
                    "Stale",
                    BrowserDownloadRestrictionMode.BLOCK_ALL,
                    BrowserPolicyScopeType.CLASSROOM,
                    null,
                    null,
                    BrowserPolicyAccountScope.ANY,
                    0,
                    now()));
            assertThat(staleUpdate).isInstanceOf(PersistenceVersionConflictException.class);

            repository.archivePolicy(policyId, 1, now());
            createPolicy(
                    repository,
                    id(),
                    fixture.classroomId,
                    BrowserDownloadRestrictionMode.NO_SPECIAL_RESTRICTIONS,
                    BrowserPolicyScopeType.CLASSROOM,
                    null,
                    null,
                    BrowserPolicyAccountScope.ANY);
            assertThat(repository.findPoliciesByClassroomId(fixture.classroomId, true)).hasSize(1);
        }
    }

    private ConfigurableApplicationContext start(Path dataDir) {
        String normalizedDataDir = dataDir.toAbsolutePath().normalize().toString().replace('\\', '/');
        return new SpringApplicationBuilder(MasterBackendApplication.class)
                .web(WebApplicationType.NONE)
                .properties(
                        "debug=false",
                        "spring.main.banner-mode=off",
                        "logging.level.root=WARN",
                        "galtek.classroom.master.storage.busy-timeout-ms=250",
                        "galtek.classroom.master.storage.enabled=true")
                .run("--galtek.classroom.master.storage.data-dir=" + normalizedDataDir);
    }

    private void migrateToV3(Path database) {
        Flyway.configure()
                .dataSource(sqliteDataSource(database))
                .locations("classpath:db/migration/sqlite")
                .target("3")
                .cleanDisabled(true)
                .load()
                .migrate();
    }

    private SQLiteDataSource sqliteDataSource(Path database) {
        SQLiteDataSource dataSource = new SQLiteDataSource();
        dataSource.setUrl("jdbc:sqlite:" + database.toAbsolutePath().normalize());
        return dataSource;
    }

    private Fixture createFixture(ConfigurableApplicationContext context) {
        String classroomId = id();
        context.getBean(ClassroomManagementService.class).create(new Classroom(
                classroomId,
                "Aula " + classroomId,
                List.of(),
                List.of(),
                List.of(),
                new ClassroomConfiguration(Set.of(), null, true, true)));

        String groupId = id();
        context.getBean(StudentManagementService.class).createGroup(
                classroomId,
                new SchoolGroup(groupId, "1", "A", "1 A", List.of()));

        String deviceId = id();
        context.getBean(DeviceManagementService.class).register(
                classroomId,
                new Device(
                        deviceId,
                        id(),
                        "PC-" + deviceId.substring(0, 4),
                        "pc-" + deviceId.substring(0, 4),
                        DeviceStatus.ONLINE,
                        OffsetDateTime.parse("2026-09-01T10:00:00Z"),
                        EnumSet.of(DeviceCapability.LOCAL_IPC, DeviceCapability.SESSION_AGENT),
                        null));
        return new Fixture(classroomId, groupId, deviceId);
    }

    private void createPolicy(
            BrowserDownloadPolicyRepository repository,
            String policyId,
            String classroomId,
            BrowserDownloadRestrictionMode restrictionMode,
            BrowserPolicyScopeType scopeType,
            String schoolGroupId,
            String deviceId,
            BrowserPolicyAccountScope accountScope) {
        repository.createPolicy(new BrowserDownloadPolicy(
                policyId,
                classroomId,
                "Policy " + policyId,
                restrictionMode,
                scopeType,
                schoolGroupId,
                deviceId,
                accountScope,
                true,
                0,
                now(),
                now()));
    }

    private int flywaySuccessCount(ConfigurableApplicationContext context) {
        Integer count = context.getBean(JdbcTemplate.class).queryForObject(
                "SELECT COUNT(*) FROM flyway_schema_history WHERE success = 1",
                Integer.class);
        return count == null ? 0 : count;
    }

    private OffsetDateTime now() {
        return OffsetDateTime.parse(nowText());
    }

    private String nowText() {
        return "2026-09-01T12:00:00Z";
    }

    private String id() {
        return UUID.randomUUID().toString();
    }

    private record Fixture(String classroomId, String groupId, String deviceId) {
    }
}
