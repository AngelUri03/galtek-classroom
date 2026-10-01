package com.galtek.classroom.operations;

import static org.assertj.core.api.Assertions.assertThat;

import java.nio.file.Path;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.springframework.jdbc.core.JdbcTemplate;
import org.sqlite.SQLiteDataSource;

class DeviceMutationHttpGuardTest {
    @TempDir Path temporaryDirectory;
    private DeviceMutationCoordinator coordinator;
    private DeviceMutationHttpGuard guard;

    @BeforeEach
    void setUp() {
        SQLiteDataSource dataSource = new SQLiteDataSource();
        dataSource.setUrl("jdbc:sqlite:" + temporaryDirectory.resolve("leases.db").toAbsolutePath());
        JdbcTemplate jdbcTemplate = new JdbcTemplate(dataSource);
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
                Clock.fixed(Instant.parse("2026-09-30T18:00:00Z"), ZoneOffset.UTC));
        guard = new DeviceMutationHttpGuard(coordinator);
    }

    @Test
    void successfulSessionMutationWaitsForAnAuthoritativeObservation() {
        String response = guard.runSession(
                List.of("device-14"),
                "SWITCH_MANAGED_ACCOUNT",
                "PRIMARY",
                () -> "response",
                ignored -> Map.of("device-14", "SUCCESS"));

        assertThat(response).isEqualTo("response");
        assertThat(coordinator.find(List.of("device-14")))
                .singleElement()
                .satisfies(state -> {
                    assertThat(state.state()).isEqualTo(DeviceMutationCoordinator.RECONCILIATION_REQUIRED);
                    assertThat(state.targetProfile()).isEqualTo("PRIMARY");
                });
    }

    @Test
    void deterministicFailureReleasesOnlyThatDeviceWhileAnotherReconciles() {
        guard.runSession(
                List.of("device-14", "device-15"),
                "SWITCH_MANAGED_ACCOUNT",
                "SECONDARY",
                () -> "response",
                ignored -> Map.of("device-14", "SUCCESS", "device-15", "FAILED"));

        assertThat(coordinator.find(List.of("device-14", "device-15")))
                .singleElement()
                .satisfies(state -> assertThat(state.deviceId()).isEqualTo("device-14"));
    }

    @Test
    void absentOrUncertainTargetResultRetainsReconciliation() {
        guard.runSession(
                List.of("device-14", "device-15"),
                "LOGOFF_WINDOWS_SESSION",
                "ADMIN",
                () -> "response",
                ignored -> Map.of("device-14", "UNKNOWN"));

        assertThat(coordinator.find(List.of("device-14", "device-15")))
                .extracting(DeviceMutationCoordinator.DeviceMutationState::deviceId)
                .containsExactly("device-14", "device-15");
    }
}
