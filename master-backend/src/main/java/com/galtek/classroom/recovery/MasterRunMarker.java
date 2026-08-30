package com.galtek.classroom.recovery;

import com.galtek.classroom.persistence.AtomicFiles;
import com.galtek.classroom.persistence.sqlite.MasterDatabasePath;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Clock;
import java.time.Instant;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.SmartLifecycle;
import org.springframework.stereotype.Component;

@Component
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class MasterRunMarker implements SmartLifecycle {

    public static final String MARKER_FILE_NAME = "master-backend.running";
    private static final Logger LOGGER = LoggerFactory.getLogger(MasterRunMarker.class);

    private final Path markerPath;
    private final Clock clock;
    private volatile boolean running;
    private volatile MasterPowerLossRecoveryState state;

    @Autowired
    public MasterRunMarker(MasterDatabasePath databasePath, Clock clock) {
        this(databasePath.dataDirectory().resolve(MARKER_FILE_NAME), clock);
    }

    public MasterRunMarker(Path markerPath, Clock clock) {
        this.markerPath = markerPath.toAbsolutePath().normalize();
        this.clock = clock;
        this.state = new MasterPowerLossRecoveryState(
                false,
                false,
                this.markerPath,
                Instant.EPOCH,
                null);
    }

    public MasterPowerLossRecoveryState state() {
        return state;
    }

    @Override
    public void start() {
        if (running) {
            return;
        }

        state = markStarted();
        if (state.previousShutdownWasUnclean()) {
            LOGGER.warn("Previous Master shutdown was not clean. SQLite and local state recovery checks will run.");
        }
        if (!state.markerWritten()) {
            LOGGER.warn(
                    "Master running marker could not be written at {}: {}",
                    state.markerPath(),
                    state.errorMessage());
        }
        running = true;
    }

    @Override
    public void stop() {
        try {
            Files.deleteIfExists(markerPath);
        } catch (IOException exception) {
            LOGGER.warn(
                    "Master running marker could not be removed at {}: {}",
                    markerPath,
                    exception.getMessage());
        } finally {
            running = false;
        }
    }

    @Override
    public boolean isRunning() {
        return running;
    }

    private MasterPowerLossRecoveryState markStarted() {
        boolean previousShutdownWasUnclean = Files.exists(markerPath);
        Instant startedAtUtc = clock.instant();

        try {
            AtomicFiles.writeAtomically(
                    markerPath,
                    true,
                    output -> output.write(markerContents(startedAtUtc).getBytes(StandardCharsets.UTF_8)));
            return new MasterPowerLossRecoveryState(
                    previousShutdownWasUnclean,
                    true,
                    markerPath,
                    startedAtUtc,
                    null);
        } catch (IOException exception) {
            return new MasterPowerLossRecoveryState(
                    previousShutdownWasUnclean,
                    false,
                    markerPath,
                    startedAtUtc,
                    exception.getMessage());
        }
    }

    private static String markerContents(Instant startedAtUtc) {
        return "schemaVersion=1%nprocess=GaltekClassroom.MasterBackend%nstartedAtUtc=%s%n"
                .formatted(startedAtUtc);
    }
}
