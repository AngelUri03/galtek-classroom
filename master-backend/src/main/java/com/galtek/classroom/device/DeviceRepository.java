package com.galtek.classroom.device;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface DeviceRepository {

    void create(String classroomId, Device device, OffsetDateTime nowUtc);

    Optional<Device> findById(String deviceId);

    List<Device> findByClassroomId(String classroomId);
}
