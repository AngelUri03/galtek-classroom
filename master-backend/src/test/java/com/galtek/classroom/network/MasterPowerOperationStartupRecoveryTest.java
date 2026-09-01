package com.galtek.classroom.network;

import static org.mockito.Mockito.verify;

import com.galtek.classroom.recovery.MasterRunMarker;
import java.nio.file.Path;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class MasterPowerOperationStartupRecoveryTest {

    @TempDir
    private Path tempDir;

    @Test
    void usesStableMasterRunMarkerStartedAtAsRecoveryCutoff() {
        Instant startedAtUtc = Instant.parse("2026-08-31T12:30:00Z");
        PowerOperationReconciliationService reconciliationService =
                org.mockito.Mockito.mock(PowerOperationReconciliationService.class);
        MasterRunMarker runMarker = new MasterRunMarker(
                tempDir.resolve(MasterRunMarker.MARKER_FILE_NAME),
                Clock.fixed(startedAtUtc, ZoneOffset.UTC));
        runMarker.start();

        MasterPowerOperationStartupRecovery recovery =
                new MasterPowerOperationStartupRecovery(reconciliationService, runMarker);

        recovery.run(null);

        verify(reconciliationService).recoverOrphanedPendingPowerTargets(startedAtUtc);
    }
}
