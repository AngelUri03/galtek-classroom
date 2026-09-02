package com.galtek.classroom.persistence.sqlite;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.assertj.core.api.Assertions.catchThrowable;

import com.galtek.classroom.MasterBackendApplication;
import com.galtek.classroom.application.ApplicationAvailability;
import com.galtek.classroom.application.ApplicationCatalogService;
import com.galtek.classroom.application.ApplicationDefinition;
import com.galtek.classroom.application.ApplicationType;
import com.galtek.classroom.application.LaunchPolicy;
import com.galtek.classroom.browser.BrowserProfile;
import com.galtek.classroom.browser.BrowserProfilePortability;
import com.galtek.classroom.browser.BrowserProfileService;
import com.galtek.classroom.browser.BrowserProfileStatus;
import com.galtek.classroom.browser.BrowserProfileStrategy;
import com.galtek.classroom.browser.BrowserType;
import com.galtek.classroom.browser.MasterBrowserProfile;
import com.galtek.classroom.browserpolicy.BrowserAccessPolicy;
import com.galtek.classroom.browserpolicy.BrowserPolicyAccountScope;
import com.galtek.classroom.browserpolicy.BrowserPolicyMode;
import com.galtek.classroom.browserpolicy.BrowserPolicyRepository;
import com.galtek.classroom.browserpolicy.BrowserPolicyScopeType;
import com.galtek.classroom.browserpolicy.BrowserUrlMatchType;
import com.galtek.classroom.browserpolicy.BrowserUrlRule;
import com.galtek.classroom.browserpolicy.BrowserUrlRuleAction;
import com.galtek.classroom.classroom.Classroom;
import com.galtek.classroom.classroom.ClassroomConfiguration;
import com.galtek.classroom.classroom.ClassroomManagementService;
import com.galtek.classroom.classroom.ClassroomRepository;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceManagementService;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.network.DeviceNetworkBinding;
import com.galtek.classroom.network.DeviceNetworkBindingRepository;
import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchOperationRepository;
import com.galtek.classroom.operations.BatchOperationService;
import com.galtek.classroom.operations.BatchOperationStatus;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationPayload;
import com.galtek.classroom.operations.OperationTarget;
import com.galtek.classroom.operations.OperationTargetType;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageException;
import com.galtek.classroom.persistence.PersistenceVersionConflictException;
import com.galtek.classroom.student.DeviceAssignment;
import com.galtek.classroom.student.DeviceAssignmentRepository;
import com.galtek.classroom.student.DeviceAssignmentService;
import com.galtek.classroom.student.DeviceAssignmentSource;
import com.galtek.classroom.student.DeviceAssignmentStatus;
import com.galtek.classroom.student.SchoolGroup;
import com.galtek.classroom.student.SchoolGroupRepository;
import com.galtek.classroom.student.Student;
import com.galtek.classroom.student.StudentManagementService;
import com.galtek.classroom.student.StudentRepository;
import com.galtek.classroom.workspace.LogicalWorkspaceDestination;
import com.galtek.classroom.workspace.StudentWorkspace;
import com.galtek.classroom.workspace.StudentWorkspaceRepository;
import com.galtek.classroom.workspace.WorkspaceMetadataService;
import com.galtek.classroom.workspace.WorkspaceRecoveryPolicy;
import com.galtek.classroom.workspace.WorkspaceStatus;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.OffsetDateTime;
import java.util.EnumSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import java.util.stream.IntStream;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.builder.SpringApplicationBuilder;
import org.springframework.context.ConfigurableApplicationContext;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;

class MasterSqlitePersistenceIntegrationTest {

    @TempDir
    private Path tempDir;

    @Test
    void createsDatabaseAppliesMigrationAndConfiguresPragmas() throws Exception {
        Path dataDir = tempDir.resolve("create");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            JdbcTemplate jdbcTemplate = context.getBean(JdbcTemplate.class);

            assertThat(context.getBean(MasterDatabasePath.class).dataDirectory())
                    .isEqualTo(dataDir.toAbsolutePath().normalize());
            assertThat(Files.exists(dataDir.resolve("classroom.db"))).isTrue();
            assertThat(jdbcTemplate.queryForObject("PRAGMA foreign_keys", Integer.class)).isEqualTo(1);
            assertThat(jdbcTemplate.queryForObject("PRAGMA journal_mode", String.class)).isEqualToIgnoringCase("wal");

            List<String> tables = jdbcTemplate.queryForList(
                    "SELECT name FROM sqlite_master WHERE type = 'table'",
                    String.class);
            assertThat(tables)
                    .contains("classrooms", "students", "devices", "device_assignments",
                            "student_workspaces", "browser_profiles", "master_browser_profiles",
                            "application_definitions", "batch_operations", "batch_target_results",
                            "device_network_bindings", "browser_access_policies", "browser_url_rules",
                            "browser_download_policies", "flyway_schema_history")
                    .doesNotContain("master_windows_binding");

            List<String> indexes = jdbcTemplate.queryForList(
                    "SELECT name FROM sqlite_master WHERE type = 'index'",
                    String.class);
            assertThat(indexes)
                    .contains("uq_device_assignments_current_student",
                            "uq_device_assignments_current_device",
                            "uq_device_network_bindings_current_device",
                            "uq_device_network_bindings_current_network_identity",
                            "uq_browser_policy_active_classroom_account",
                            "uq_browser_policy_active_group_account",
                            "uq_browser_policy_active_device_account",
                            "uq_browser_download_policy_active_classroom_account",
                            "uq_browser_download_policy_active_group_account",
                            "uq_browser_download_policy_active_device_account",
                            "ix_browser_url_rules_policy",
                            "ix_batch_target_results_operation_status");

            List<String> bindingColumns = jdbcTemplate.queryForList(
                    "SELECT name FROM pragma_table_info('device_network_bindings')",
                    String.class);
            assertThat(bindingColumns)
                    .contains("device_id", "installation_id", "network_identity_id",
                            "public_key_fingerprint", "agent_version", "capabilities_json",
                            "registered_at_utc", "last_connected_at_utc")
                    .noneMatch(column -> column.equalsIgnoreCase("private_key"))
                    .noneMatch(column -> column.equalsIgnoreCase("password"))
                    .noneMatch(column -> column.equalsIgnoreCase("jwt"))
                    .noneMatch(column -> column.equalsIgnoreCase("secret"));

            List<String> policyColumns = jdbcTemplate.queryForList(
                    "SELECT name FROM pragma_table_info('browser_access_policies')",
                    String.class);
            assertThat(policyColumns)
                    .contains("policy_id", "classroom_id", "name", "mode", "scope_type",
                            "school_group_id", "device_id", "account_scope", "active", "version")
                    .noneMatch(column -> column.equalsIgnoreCase("script"))
                    .noneMatch(column -> column.equalsIgnoreCase("command"))
                    .noneMatch(column -> column.equalsIgnoreCase("arguments"))
                    .noneMatch(column -> column.equalsIgnoreCase("proxy"))
                    .noneMatch(column -> column.equalsIgnoreCase("executable"))
                    .noneMatch(column -> column.equalsIgnoreCase("path"));

            List<String> downloadPolicyColumns = jdbcTemplate.queryForList(
                    "SELECT name FROM pragma_table_info('browser_download_policies')",
                    String.class);
            assertThat(downloadPolicyColumns)
                    .contains("policy_id", "classroom_id", "name", "restriction_mode", "scope_type",
                            "school_group_id", "device_id", "account_scope", "active", "version")
                    .noneMatch(column -> column.toLowerCase().contains("extension"))
                    .noneMatch(column -> column.toLowerCase().contains("mime"))
                    .noneMatch(column -> column.equalsIgnoreCase("script"))
                    .noneMatch(column -> column.equalsIgnoreCase("command"))
                    .noneMatch(column -> column.equalsIgnoreCase("path"));
        }
    }

    @Test
    void reopensPersistedDomainData() {
        Path dataDir = tempDir.resolve("reopen");
        Fixture fixture;

        try (ConfigurableApplicationContext context = start(dataDir)) {
            fixture = createFixture(context);
            context.getBean(DeviceAssignmentService.class)
                    .assignStudent(fixture.studentId, fixture.deviceId, DeviceAssignmentSource.MANUAL);
        }

        try (ConfigurableApplicationContext reopened = start(dataDir)) {
            assertThat(reopened.getBean(ClassroomRepository.class).findById(fixture.classroomId))
                    .get()
                    .extracting(Classroom::displayName)
                    .isEqualTo("Aula Primaria");
            assertThat(reopened.getBean(SchoolGroupRepository.class).findById(fixture.groupId))
                    .get()
                    .extracting(SchoolGroup::studentIds)
                    .satisfies(studentIds -> assertThat(studentIds).containsExactly(fixture.studentId));
            assertThat(reopened.getBean(StudentRepository.class).findById(fixture.studentId))
                    .get()
                    .satisfies(student -> {
                        assertThat(student.displayName()).isEqualTo("Alex R.");
                        assertThat(student.currentDeviceAssignment()).isNotNull();
                        assertThat(student.currentDeviceAssignment().deviceId()).isEqualTo(fixture.deviceId);
                    });
            assertThat(reopened.getBean(DeviceRepository.class).findById(fixture.deviceId))
                    .get()
                    .satisfies(device -> assertThat(device.assignedStudentId()).isEqualTo(fixture.studentId));
            assertThat(reopened.getBean(StudentWorkspaceRepository.class).findByStudentId(fixture.studentId))
                    .get()
                    .extracting(StudentWorkspace::status)
                    .isEqualTo(WorkspaceStatus.READY);
        }
    }

    @Test
    void restartPreservesDeviceNetworkBinding() {
        Path dataDir = tempDir.resolve("network-binding-reopen");
        BindingFixture saved;
        UUID networkIdentityId = UUID.randomUUID();
        OffsetDateTime registeredAt = OffsetDateTime.parse("2026-08-27T17:00:00Z");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            UUID installationId = UUID.fromString(context.getBean(DeviceRepository.class)
                    .findById(fixture.deviceId)
                    .orElseThrow()
                    .installationId());
            context.getBean(DeviceNetworkBindingRepository.class).create(new DeviceNetworkBinding(
                    UUID.randomUUID().toString(),
                    fixture.deviceId,
                    installationId,
                    networkIdentityId,
                    "a".repeat(64),
                    "0.5.0-test",
                    EnumSet.of(DeviceCapability.HEARTBEAT_V1, DeviceCapability.OPERATION_FRAMEWORK_V1),
                    registeredAt,
                    registeredAt.plusSeconds(5),
                    true,
                    0), registeredAt);
            saved = new BindingFixture(fixture.classroomId, fixture.deviceId, installationId);
        }

        try (ConfigurableApplicationContext reopened = start(dataDir)) {
            assertThat(reopened.getBean(DeviceNetworkBindingRepository.class)
                    .findCurrentByNetworkIdentityId(networkIdentityId))
                    .get()
                    .satisfies(binding -> {
                        assertThat(binding.deviceId()).isEqualTo(saved.deviceId());
                        assertThat(binding.classroomId()).isEqualTo(saved.classroomId());
                        assertThat(binding.installationId()).isEqualTo(saved.installationId());
                        assertThat(binding.capabilities())
                                .containsExactlyInAnyOrder(
                                        DeviceCapability.HEARTBEAT_V1,
                                        DeviceCapability.OPERATION_FRAMEWORK_V1);
                    });
        }
    }

    @Test
    void reopensPersistedBrowserPoliciesAndRules() {
        Path dataDir = tempDir.resolve("browser-policy-reopen");
        String policyId = id();
        String ruleId = id();

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            OffsetDateTime now = OffsetDateTime.parse("2026-09-01T12:00:00Z");
            BrowserPolicyRepository repository = context.getBean(BrowserPolicyRepository.class);
            repository.createPolicy(new BrowserAccessPolicy(
                    policyId,
                    fixture.classroomId,
                    "Primary allowlist",
                    BrowserPolicyMode.ALLOWLIST,
                    BrowserPolicyScopeType.CLASSROOM,
                    null,
                    null,
                    BrowserPolicyAccountScope.PRIMARY,
                    true,
                    0,
                    now,
                    now));
            repository.createRule(new BrowserUrlRule(
                    ruleId,
                    policyId,
                    BrowserUrlRuleAction.ALLOW,
                    BrowserUrlMatchType.HOST_SUFFIX,
                    "school.local",
                    true,
                    "LAN content",
                    0,
                    now,
                    now));
        }

        try (ConfigurableApplicationContext reopened = start(dataDir)) {
            BrowserPolicyRepository repository = reopened.getBean(BrowserPolicyRepository.class);
            assertThat(repository.findPolicyById(policyId))
                    .get()
                    .satisfies(policy -> {
                        assertThat(policy.mode()).isEqualTo(BrowserPolicyMode.ALLOWLIST);
                        assertThat(policy.accountScope()).isEqualTo(BrowserPolicyAccountScope.PRIMARY);
                    });
            assertThat(repository.findRulesByPolicyId(policyId, true))
                    .singleElement()
                    .satisfies(rule -> {
                        assertThat(rule.ruleId()).isEqualTo(ruleId);
                        assertThat(rule.pattern()).isEqualTo("school.local");
                    });
        }
    }

    @Test
    void migrationIsIdempotentAcrossRestarts() {
        Path dataDir = tempDir.resolve("idempotence");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            assertThat(flywaySuccessCount(context)).isEqualTo(4);
        }

        try (ConfigurableApplicationContext context = start(dataDir)) {
            assertThat(flywaySuccessCount(context)).isEqualTo(4);
            assertThat(context.getBean(ClassroomRepository.class).findActive()).isEmpty();
        }
    }

    @Test
    void foreignKeysAreEnabledAndInvalidReferencesFail() {
        Path dataDir = tempDir.resolve("foreign-keys");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            JdbcTemplate jdbcTemplate = context.getBean(JdbcTemplate.class);
            assertThat(jdbcTemplate.queryForObject("PRAGMA foreign_keys", Integer.class)).isEqualTo(1);

            assertThatThrownBy(() -> jdbcTemplate.update("""
                    INSERT INTO school_groups (
                        group_id, classroom_id, grade, section, display_name,
                        active, created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, ?, ?, ?, 1, ?, ?, 0)
                    """,
                    id(), id(), "1", "A", "1 A",
                    "2026-08-26T00:00:00Z", "2026-08-26T00:00:00Z"))
                    .isInstanceOf(DataAccessException.class);
        }
    }

    @Test
    void serviceAndDatabaseRejectDuplicateCurrentAssignments() {
        Path dataDir = tempDir.resolve("assignment-unique");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            String secondStudentId = createStudent(context, fixture.classroomId, fixture.groupId, "Bri", "Lopez");
            String secondDeviceId = createDevice(context, fixture.classroomId, "PC02");
            DeviceAssignmentService service = context.getBean(DeviceAssignmentService.class);
            DeviceAssignmentRepository repository = context.getBean(DeviceAssignmentRepository.class);

            service.assignStudent(fixture.studentId, fixture.deviceId, DeviceAssignmentSource.MANUAL);

            Throwable sameStudent = catchThrowable(
                    () -> service.assignStudent(fixture.studentId, secondDeviceId, DeviceAssignmentSource.MANUAL));
            assertThat(sameStudent).isInstanceOf(MasterStorageException.class);
            assertThat(((MasterStorageException) sameStudent).errorCode())
                    .isEqualTo(ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION);

            Throwable sameDevice = catchThrowable(
                    () -> service.assignStudent(secondStudentId, fixture.deviceId, DeviceAssignmentSource.MANUAL));
            assertThat(sameDevice).isInstanceOf(MasterStorageException.class);
            assertThat(((MasterStorageException) sameDevice).errorCode())
                    .isEqualTo(ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION);

            OffsetDateTime now = OffsetDateTime.parse("2026-08-26T12:00:00Z");
            Throwable dbConstraint = catchThrowable(() -> repository.create(
                    id(),
                    new DeviceAssignment(
                            fixture.studentId,
                            secondDeviceId,
                            now,
                            DeviceAssignmentStatus.CURRENT,
                            DeviceAssignmentSource.MANUAL,
                            true),
                    now));
            assertThat(dbConstraint).isInstanceOf(MasterStorageException.class);
            assertThat(((MasterStorageException) dbConstraint).errorCode())
                    .isEqualTo(ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION);
        }
    }

    @Test
    void movingAssignmentPreservesHistory() {
        Path dataDir = tempDir.resolve("assignment-history");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            String secondDeviceId = createDevice(context, fixture.classroomId, "PC12");
            DeviceAssignmentService service = context.getBean(DeviceAssignmentService.class);
            DeviceAssignmentRepository repository = context.getBean(DeviceAssignmentRepository.class);

            service.assignStudent(fixture.studentId, fixture.deviceId, DeviceAssignmentSource.MANUAL);
            service.moveCurrentAssignment(fixture.studentId, secondDeviceId, DeviceAssignmentSource.MOVE_WORKFLOW);

            var history = repository.findHistoryByStudentId(fixture.studentId);
            assertThat(history).hasSize(2);
            assertThat(history.get(0).assignment().deviceId()).isEqualTo(fixture.deviceId);
            assertThat(history.get(0).assignment().current()).isFalse();
            assertThat(history.get(0).assignment().status()).isEqualTo(DeviceAssignmentStatus.ENDED);
            assertThat(history.get(0).endedAtUtc()).isNotNull();
            assertThat(history.get(1).assignment().deviceId()).isEqualTo(secondDeviceId);
            assertThat(history.get(1).assignment().current()).isTrue();
        }
    }

    @Test
    void archiveHidesStudentButKeepsHistory() {
        Path dataDir = tempDir.resolve("archive");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            context.getBean(DeviceAssignmentService.class)
                    .assignStudent(fixture.studentId, fixture.deviceId, DeviceAssignmentSource.MANUAL);

            StudentRepository studentRepository = context.getBean(StudentRepository.class);
            long version = studentRepository.versionOf(fixture.studentId);
            context.getBean(StudentManagementService.class).archive(fixture.studentId, version);

            assertThat(studentRepository.findActiveByClassroomId(fixture.classroomId))
                    .extracting(Student::studentId)
                    .doesNotContain(fixture.studentId);
            assertThat(studentRepository.findById(fixture.studentId))
                    .get()
                    .extracting(Student::active)
                    .isEqualTo(false);
            assertThat(context.getBean(DeviceAssignmentRepository.class).findHistoryByStudentId(fixture.studentId))
                    .hasSize(1);
        }
    }

    @Test
    void browserProfileSchemaPersistsMetadataAndExcludesSecrets() {
        Path dataDir = tempDir.resolve("browser-profile");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            assertThat(context.getBean(BrowserProfileService.class).findStudentProfile(fixture.browserProfileId))
                    .get()
                    .extracting(BrowserProfile::profileReference)
                    .isEqualTo("student-managed-profile");

            JdbcTemplate jdbcTemplate = context.getBean(JdbcTemplate.class);
            List<String> columns = jdbcTemplate.queryForList(
                    "SELECT name FROM pragma_table_info('browser_profiles')",
                    String.class);
            columns.addAll(jdbcTemplate.queryForList(
                    "SELECT name FROM pragma_table_info('master_browser_profiles')",
                    String.class));
            assertThat(columns)
                    .noneMatch(column -> column.equalsIgnoreCase("password"))
                    .noneMatch(column -> column.equalsIgnoreCase("cookie"))
                    .noneMatch(column -> column.equalsIgnoreCase("token"))
                    .noneMatch(column -> column.equalsIgnoreCase("login_data"))
                    .noneMatch(column -> column.equalsIgnoreCase("loginData"))
                    .noneMatch(column -> column.equalsIgnoreCase("local_state_secret"));
        }
    }

    @Test
    void batchOperationsPersistResultsAndRetryableFailuresAcrossReopen() {
        Path dataDir = tempDir.resolve("batch");
        String classroomId;
        String operationId = id();

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            classroomId = fixture.classroomId;
            BatchOperation operation = BatchOperation.fromTargets(
                    operationId,
                    OperationType.OPEN_APPLICATION,
                    "SID-S-1-5-21-1000",
                    OffsetDateTime.parse("2026-08-26T13:00:00Z"),
                    batchTargets());
            context.getBean(BatchOperationService.class).create(
                    classroomId,
                    operation,
                    new OperationPayload(1, "{\"schemaVersion\":1,\"applicationId\":\"math-app\"}"));
        }

        try (ConfigurableApplicationContext reopened = start(dataDir)) {
            BatchOperation operation = reopened.getBean(BatchOperationRepository.class)
                    .findById(operationId)
                    .orElseThrow();
            assertThat(operation.status()).isEqualTo(BatchOperationStatus.PARTIAL_SUCCESS);
            assertThat(operation.targets()).hasSize(25);
            assertThat(operation.targets())
                    .filteredOn(target -> target.status() == TargetExecutionStatus.SUCCESS)
                    .hasSize(22);
            assertThat(operation.targets())
                    .filteredOn(target -> target.status() == TargetExecutionStatus.FAILED)
                    .hasSize(3);
            assertThat(reopened.getBean(BatchOperationService.class).retryableFailures(operationId))
                    .extracting(result -> result.target().displayName())
                    .containsExactly("PC23", "PC24");
        }
    }

    @Test
    void multiWriteBatchOperationRollsBackOnTargetFailure() {
        Path dataDir = tempDir.resolve("rollback");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            String operationId = id();
            var duplicateTarget = new OperationTarget(OperationTargetType.DEVICE, id(), "PC01");
            BatchOperation operation = BatchOperation.fromTargets(
                    operationId,
                    OperationType.OPEN_URL,
                    "SID-S-1-5-21-1000",
                    OffsetDateTime.parse("2026-08-26T14:00:00Z"),
                    List.of(
                            new BatchTargetResult(duplicateTarget, TargetExecutionStatus.SUCCESS, null, "ok", 1),
                            new BatchTargetResult(duplicateTarget, TargetExecutionStatus.SUCCESS, null, "ok", 1)));

            Throwable throwable = catchThrowable(() -> context.getBean(BatchOperationService.class)
                    .create(fixture.classroomId, operation, OperationPayload.none()));

            assertThat(throwable).isInstanceOf(MasterStorageException.class);
            assertThat(context.getBean(BatchOperationRepository.class).findById(operationId)).isEmpty();
        }
    }

    @Test
    void optimisticVersionRejectsStaleUpdates() {
        Path dataDir = tempDir.resolve("optimistic-version");

        try (ConfigurableApplicationContext context = start(dataDir)) {
            Fixture fixture = createFixture(context);
            ClassroomRepository repository = context.getBean(ClassroomRepository.class);
            ClassroomManagementService service = context.getBean(ClassroomManagementService.class);

            long version = repository.versionOf(fixture.classroomId);
            service.rename(fixture.classroomId, "Aula Nueva", version);

            assertThatThrownBy(() -> service.rename(fixture.classroomId, "Aula Vieja", version))
                    .isInstanceOf(PersistenceVersionConflictException.class);
            assertThat(repository.versionOf(fixture.classroomId)).isEqualTo(version + 1);
        }
    }

    @Test
    void corruptDatabaseIsDetectedAndPreserved() throws Exception {
        Path dataDir = tempDir.resolve("corrupt");
        Files.createDirectories(dataDir);
        Path database = dataDir.resolve("classroom.db");
        Files.writeString(database, "not-a-sqlite-database");

        Throwable throwable = catchThrowable(() -> {
            try (ConfigurableApplicationContext ignored = start(dataDir)) {
                // Startup is expected to fail before the context is usable.
            }
        });

        MasterStorageException storageException = findStorageException(throwable);
        assertThat(storageException).isNotNull();
        assertThat(storageException.errorCode()).isEqualTo(ErrorCode.MASTER_DATABASE_CORRUPT);
        assertThat(Files.readString(database)).isEqualTo("not-a-sqlite-database");
    }

    @Test
    void masterDataDirEnvironmentOverrideResolvesBeforeCommonApplicationData() {
        Path override = tempDir.resolve("env-override");
        var resolver = new MasterDataDirectoryResolver(
                () -> tempDir.resolve("program-data"),
                name -> MasterDataDirectoryResolver.ENVIRONMENT_OVERRIDE.equals(name)
                        ? override.toString()
                        : null);

        assertThat(resolver.resolve("")).isEqualTo(override.toAbsolutePath().normalize());
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

    private Fixture createFixture(ConfigurableApplicationContext context) {
        String applicationId = id();
        context.getBean(ApplicationCatalogService.class).create(new ApplicationDefinition(
                applicationId,
                "Math Practice",
                ApplicationType.EDUCATIONAL_CONTENT,
                ApplicationAvailability.REQUIRED,
                LaunchPolicy.ALLOWED));

        String classroomId = id();
        context.getBean(ClassroomManagementService.class).create(new Classroom(
                classroomId,
                "Aula Primaria",
                List.of(),
                List.of(),
                List.of(),
                new ClassroomConfiguration(Set.of(applicationId), null, true, true)));

        String groupId = id();
        context.getBean(StudentManagementService.class).createGroup(
                classroomId,
                new SchoolGroup(groupId, "1", "A", "1 A", List.of()));

        String studentId = createStudent(context, classroomId, groupId, "Alex", "Ramos");
        String deviceId = createDevice(context, classroomId, "PC01");

        String masterProfileId = id();
        context.getBean(BrowserProfileService.class).createMasterProfile(
                new MasterBrowserProfile(
                        masterProfileId,
                        BrowserType.CHROME,
                        "Teacher Chrome",
                        "teacher-managed-profile",
                        BrowserProfileStatus.READY),
                BrowserProfileStrategy.MANAGED_PROFILE,
                "S-1-5-21-1000",
                true);

        return new Fixture(
                classroomId,
                groupId,
                studentId,
                "workspace-" + studentId,
                "browser-" + studentId,
                deviceId,
                applicationId,
                masterProfileId);
    }

    private String createStudent(
            ConfigurableApplicationContext context,
            String classroomId,
            String groupId,
            String firstName,
            String lastName) {
        String studentId = id();
        String workspaceId = "workspace-" + studentId;
        String browserProfileId = "browser-" + studentId;

        context.getBean(StudentManagementService.class).register(
                classroomId,
                groupId,
                new Student(
                        studentId,
                        firstName,
                        lastName,
                        firstName + " " + lastName.charAt(0) + ".",
                        "1",
                        "A",
                        true,
                        workspaceId,
                        browserProfileId,
                        null));
        context.getBean(WorkspaceMetadataService.class).create(new StudentWorkspace(
                workspaceId,
                studentId,
                WorkspaceStatus.READY,
                EnumSet.of(
                        LogicalWorkspaceDestination.WORKSPACE_ROOT,
                        LogicalWorkspaceDestination.DOCUMENTS,
                        LogicalWorkspaceDestination.HOMEWORK,
                        LogicalWorkspaceDestination.WORK,
                        LogicalWorkspaceDestination.DOWNLOADS,
                        LogicalWorkspaceDestination.DESKTOP),
                browserProfileId,
                WorkspaceRecoveryPolicy.plannedDefaults()));
        context.getBean(BrowserProfileService.class).createStudentProfile(new BrowserProfile(
                browserProfileId,
                studentId,
                BrowserType.CHROME,
                firstName + " Chrome",
                BrowserProfileStrategy.MANAGED_PROFILE,
                "student-managed-profile",
                BrowserProfileStatus.READY,
                BrowserProfilePortability.REAUTH_REQUIRED));

        return studentId;
    }

    private String createDevice(ConfigurableApplicationContext context, String classroomId, String displayName) {
        String deviceId = id();
        context.getBean(DeviceManagementService.class).register(
                classroomId,
                new Device(
                        deviceId,
                        id(),
                        displayName,
                        displayName.toLowerCase(),
                        DeviceStatus.ONLINE,
                        OffsetDateTime.parse("2026-08-26T10:00:00Z"),
                        EnumSet.of(DeviceCapability.LOCAL_IPC, DeviceCapability.SESSION_AGENT),
                        null));
        return deviceId;
    }

    private List<BatchTargetResult> batchTargets() {
        return IntStream.rangeClosed(1, 25)
                .mapToObj(number -> {
                    String displayName = "PC%02d".formatted(number);
                    OperationTarget target = new OperationTarget(OperationTargetType.DEVICE, id(), displayName);
                    if (number <= 22) {
                        return new BatchTargetResult(target, TargetExecutionStatus.SUCCESS, null, "ok", 1);
                    }
                    ErrorCode errorCode = number == 25
                            ? ErrorCode.APPLICATION_NOT_INSTALLED
                            : (number == 23 ? ErrorCode.DEVICE_OFFLINE : ErrorCode.AGENT_UNAVAILABLE);
                    return new BatchTargetResult(target, TargetExecutionStatus.FAILED, errorCode, "failed", 1);
                })
                .toList();
    }

    private int flywaySuccessCount(ConfigurableApplicationContext context) {
        Integer count = context.getBean(JdbcTemplate.class).queryForObject(
                "SELECT COUNT(*) FROM flyway_schema_history WHERE success = 1",
                Integer.class);
        return count == null ? 0 : count;
    }

    private MasterStorageException findStorageException(Throwable throwable) {
        Throwable current = throwable;
        while (current != null) {
            if (current instanceof MasterStorageException storageException) {
                return storageException;
            }
            current = current.getCause();
        }
        return null;
    }

    private String id() {
        return UUID.randomUUID().toString();
    }

    private record Fixture(
            String classroomId,
            String groupId,
            String studentId,
            String workspaceId,
            String browserProfileId,
            String deviceId,
            String applicationId,
            String masterProfileId) {
    }

    private record BindingFixture(
            String classroomId,
            String deviceId,
            UUID installationId) {
    }
}
