package com.galtek.classroom.operations;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.galtek.classroom.api.ApiException;
import java.nio.file.Path;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.springframework.jdbc.core.JdbcTemplate;
import org.sqlite.SQLiteDataSource;

class DeviceMutationCoordinatorTest {
    @TempDir Path temporaryDirectory;
    private JdbcTemplate jdbcTemplate;
    private DeviceMutationCoordinator coordinator;

    @BeforeEach
    void setUp() {
        SQLiteDataSource dataSource = new SQLiteDataSource();
        dataSource.setUrl("jdbc:sqlite:" + temporaryDirectory.resolve("leases.db").toAbsolutePath());
        jdbcTemplate = new JdbcTemplate(dataSource);
        jdbcTemplate.execute("""
                CREATE TABLE device_mutation_leases (
                    device_id TEXT PRIMARY KEY,
                    state TEXT NOT NULL,
                    operation_type TEXT NOT NULL,
                    target_profile TEXT,
                    started_at_utc TEXT NOT NULL
                )
                """);
        coordinator = new DeviceMutationCoordinator(
                jdbcTemplate,
                Clock.fixed(Instant.parse("2026-09-29T18:00:00Z"), ZoneOffset.UTC));
    }

    @Test
    void acquiresPerDeviceAndAllowsDifferentDevices() {
        coordinator.acquire(List.of("device-14"), "SWITCH_MANAGED_ACCOUNT", "PRIMARY");
        coordinator.acquire(List.of("device-15"), "RESTART", null);

        assertThat(coordinator.find(List.of("device-14", "device-15")))
                .extracting(DeviceMutationCoordinator.DeviceMutationState::deviceId)
                .containsExactly("device-14", "device-15");
    }

    @Test
    void rejectsWholeBatchWhenOneTargetIsBusy() {
        coordinator.acquire(List.of("device-14"), "SWITCH_MANAGED_ACCOUNT", "PRIMARY");

        assertThatThrownBy(() -> coordinator.acquire(
                List.of("device-15", "device-14"), "RESTART", null))
                .isInstanceOfSatisfying(ApiException.class, exception -> {
                    assertThat(exception.status().value()).isEqualTo(409);
                    assertThat(exception.code()).isEqualTo(ErrorCode.DEVICE_OPERATION_IN_PROGRESS.name());
                });
        assertThat(coordinator.find(List.of("device-15"))).isEmpty();
    }

    @Test
    void reconciliationSurvivesCoordinatorRecreationUntilStableRead() {
        DeviceMutationCoordinator.Lease lease = coordinator.acquire(
                List.of("device-14"), "SWITCH_MANAGED_ACCOUNT", "SECONDARY");
        coordinator.requireReconciliation(lease);

        DeviceMutationCoordinator recreated = new DeviceMutationCoordinator(
                jdbcTemplate,
                Clock.fixed(Instant.parse("2026-09-29T18:01:00Z"), ZoneOffset.UTC));
        assertThat(recreated.find(List.of("device-14")))
                .singleElement()
                .satisfies(state -> assertThat(state.state())
                        .isEqualTo(DeviceMutationCoordinator.RECONCILIATION_REQUIRED));

        recreated.confirmStable(List.of("device-14"), List.of("SWITCH_MANAGED_ACCOUNT"));
        assertThat(recreated.find(List.of("device-14"))).isEmpty();
    }

    @Test
    void stableReadOnlyClearsTheOperationFamilyItCanAuthoritativelyConfirm() {
        DeviceMutationCoordinator.Lease lease = coordinator.acquire(
                List.of("device-14"), "RESTART", null);
        coordinator.requireReconciliation(lease);

        coordinator.confirmStable(List.of("device-14"), List.of("SWITCH_MANAGED_ACCOUNT"));
        assertThat(coordinator.find(List.of("device-14"))).hasSize(1);

        coordinator.confirmStable(List.of("device-14"), List.of("RESTART"));
        assertThat(coordinator.find(List.of("device-14"))).isEmpty();
    }

    @Test
    void terminalOutcomeReleasesLease() {
        DeviceMutationCoordinator.Lease lease = coordinator.acquire(
                List.of("device-14"), "LOCK_INPUT", null);
        coordinator.complete(lease);
        assertThat(coordinator.find(List.of("device-14"))).isEmpty();
    }
}
