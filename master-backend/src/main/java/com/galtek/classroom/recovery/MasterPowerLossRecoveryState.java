package com.galtek.classroom.recovery;

import java.nio.file.Path;
import java.time.Instant;

public record MasterPowerLossRecoveryState(
        boolean previousShutdownWasUnclean,
        boolean markerWritten,
        Path markerPath,
        Instant startedAtUtc,
        String errorMessage) {
}
