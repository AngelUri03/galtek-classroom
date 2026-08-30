package com.galtek.classroom.recovery;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.network.PairingConstants;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.performance.ResourceWorkClass;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneId;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class PowerLossRecoveryPolicyTest {

    private static final Instant FIXED_NOW = Instant.parse("2026-08-29T12:00:00Z");

    @TempDir
    private Path tempDir;

    @Test
    void runMarkerWhenStoppedCleanlyNextStartupReportsClean() {
        MasterRunMarker first = marker(tempDir.resolve("clean"));

        first.start();
        first.stop();
        MasterRunMarker second = marker(tempDir.resolve("clean"));
        second.start();

        assertThat(first.state().previousShutdownWasUnclean()).isFalse();
        assertThat(second.state().previousShutdownWasUnclean()).isFalse();
        assertThat(second.state().markerWritten()).isTrue();
        second.stop();
    }

    @Test
    void runMarkerWhenPreviousMarkerRemainsReportsUncleanShutdown() {
        MasterRunMarker first = marker(tempDir.resolve("unclean"));
        first.start();

        MasterRunMarker second = marker(tempDir.resolve("unclean"));
        second.start();

        assertThat(second.state().previousShutdownWasUnclean()).isTrue();
        assertThat(second.state().markerWritten()).isTrue();
        second.stop();
    }

    @Test
    void runMarkerDoesNotWritePeriodically() throws Exception {
        MasterRunMarker marker = marker(tempDir.resolve("no-periodic-writes"));
        marker.start();
        String before = Files.readString(marker.state().markerPath());

        marker.state();
        marker.state();
        String after = Files.readString(marker.state().markerPath());

        assertThat(after).isEqualTo(before);
        marker.stop();
    }

    @Test
    void recoveryMarkerDoesNotDeleteDatabaseWalShmOrCriticalFiles() throws Exception {
        Path dataDir = tempDir.resolve("preserve");
        Files.createDirectories(dataDir);
        Path database = dataDir.resolve("classroom.db");
        Path wal = dataDir.resolve("classroom.db-wal");
        Path shm = dataDir.resolve("classroom.db-shm");
        Path masterIdentity = dataDir.resolve(PairingConstants.MASTER_NETWORK_IDENTITY_FILE_NAME);
        Path masterKey = dataDir.resolve(PairingConstants.MASTER_NETWORK_PRIVATE_KEY_FILE_NAME);
        Path masterProtector = dataDir.resolve(PairingConstants.MASTER_NETWORK_PROTECTOR_FILE_NAME);
        Path trust = dataDir.resolve(PairingConstants.PAIRED_CLIENTS_FILE_NAME);
        Files.writeString(database, "db");
        Files.writeString(wal, "wal");
        Files.writeString(shm, "shm");
        Files.writeString(masterIdentity, "identity");
        Files.writeString(masterKey, "key");
        Files.writeString(masterProtector, "protector");
        Files.writeString(trust, "trust");

        MasterRunMarker marker = marker(dataDir);
        marker.start();

        assertThat(Files.exists(database)).isTrue();
        assertThat(Files.exists(wal)).isTrue();
        assertThat(Files.exists(shm)).isTrue();
        assertThat(Files.exists(masterIdentity)).isTrue();
        assertThat(Files.exists(masterKey)).isTrue();
        assertThat(Files.exists(masterProtector)).isTrue();
        assertThat(Files.exists(trust)).isTrue();
        marker.stop();
    }

    @Test
    void masterCanBeControlPlaneReadyWithZeroClients() {
        MasterStartupReadiness readiness = new MasterStartupReadiness(true, true, 0);

        assertThat(readiness.controlPlaneReady()).isTrue();
        assertThat(readiness.waitsForClients()).isFalse();
    }

    @Test
    void startupPolicyProhibitsAutomaticCaptureForBootEvents() {
        StartupWorkPolicy policy = new StartupWorkPolicy();

        for (StartupEvent event : StartupEvent.values()) {
            assertThat(policy.automaticCaptureAllowed(event)).isFalse();
        }
    }

    @Test
    void startupPolicyDefersVisualTransferAndBackgroundDuringRecovery() {
        StartupWorkPolicy policy = new StartupWorkPolicy();

        assertThat(policy.allowedDuringControlPlaneStartup(ResourceWorkClass.CONTROL_CRITICAL)).isTrue();
        assertThat(policy.allowedDuringControlPlaneStartup(ResourceWorkClass.VISUAL)).isFalse();
        assertThat(policy.deferredDuringRecovery(ResourceWorkClass.VISUAL)).isTrue();
        assertThat(policy.deferredDuringRecovery(ResourceWorkClass.TRANSFER)).isTrue();
        assertThat(policy.deferredDuringRecovery(ResourceWorkClass.BACKGROUND)).isTrue();
    }

    @Test
    void uncertainRemoteOperationIsNotModeledAsSuccess() {
        RemoteOperationRecoveryPolicy policy = new RemoteOperationRecoveryPolicy();

        assertThat(policy.decide(
                RemoteOperationDeliveryState.REQUEST_SENT_ACK_MISSING,
                TargetExecutionStatus.SUCCESS))
                .isEqualTo(RemoteOperationRecoveryDecision.RECONCILE_REQUIRED);
        assertThat(policy.decide(
                RemoteOperationDeliveryState.ACCEPTED_RESULT_MISSING,
                TargetExecutionStatus.SUCCESS))
                .isEqualTo(RemoteOperationRecoveryDecision.RECONCILE_REQUIRED);
        assertThat(policy.decide(
                RemoteOperationDeliveryState.RESULT_CONFIRMED,
                TargetExecutionStatus.SUCCESS))
                .isEqualTo(RemoteOperationRecoveryDecision.CONFIRMED_SUCCESS);
    }

    private static MasterRunMarker marker(Path dataDir) {
        return new MasterRunMarker(
                dataDir.resolve(MasterRunMarker.MARKER_FILE_NAME),
                Clock.fixed(FIXED_NOW, ZoneId.of("UTC")));
    }
}
