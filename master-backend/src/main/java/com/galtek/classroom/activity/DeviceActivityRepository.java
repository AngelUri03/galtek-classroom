package com.galtek.classroom.activity;

import java.time.OffsetDateTime;
import java.util.List;

public interface DeviceActivityRepository {
    void add(DeviceActivityEvent event);

    List<DeviceActivityEvent> find(
            String classroomId,
            String deviceId,
            OffsetDateTime fromInclusive,
            OffsetDateTime toExclusive,
            int limit);
}
