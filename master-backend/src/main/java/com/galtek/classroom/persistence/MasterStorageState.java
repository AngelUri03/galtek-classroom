package com.galtek.classroom.persistence;

import java.util.concurrent.atomic.AtomicReference;
import org.springframework.stereotype.Component;

@Component
public class MasterStorageState {

    private final AtomicReference<MasterStorageHealth> health =
            new AtomicReference<>(MasterStorageHealth.unavailable());

    public MasterStorageHealth health() {
        return health.get();
    }

    public void markReady() {
        health.set(MasterStorageHealth.ready());
    }

    public void mark(MasterStorageStatus status, String errorCode) {
        health.set(new MasterStorageHealth(status, errorCode));
    }
}
