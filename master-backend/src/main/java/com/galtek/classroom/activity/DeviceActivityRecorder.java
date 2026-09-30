package com.galtek.classroom.activity;

import java.time.Clock;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.UUID;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled",
        havingValue = "true", matchIfMissing = true)
public class DeviceActivityRecorder {
    private final DeviceActivityRepository repository;
    private final Clock clock;

    public DeviceActivityRecorder(DeviceActivityRepository repository, Clock clock) {
        this.repository = repository;
        this.clock = clock;
    }

    public void record(
            String classroomId,
            String deviceId,
            String eventType,
            String actor,
            String role,
            String accountReference,
            String result,
            String message) {
        repository.add(new DeviceActivityEvent(
                UUID.randomUUID().toString(), classroomId, deviceId, eventType,
                clean(actor, "LOCAL_MASTER"),
                OffsetDateTime.ofInstant(clock.instant(), ZoneOffset.UTC),
                clean(role, null), clean(accountReference, null), clean(result, "UNKNOWN"),
                clean(message, null)));
    }

    private static String clean(String value, String fallback) {
        return value == null || value.isBlank() ? fallback : value.trim();
    }
}
