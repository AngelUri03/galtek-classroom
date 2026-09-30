package com.galtek.classroom.activity;

import java.time.OffsetDateTime;

public record DeviceActivityEvent(
        String eventId,
        String classroomId,
        String deviceId,
        String eventType,
        String actor,
        OffsetDateTime occurredAtUtc,
        String role,
        String accountReference,
        String result,
        String message) {
}
