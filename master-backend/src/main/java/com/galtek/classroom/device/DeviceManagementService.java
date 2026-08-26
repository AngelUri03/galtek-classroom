package com.galtek.classroom.device;

import java.time.Clock;
import java.time.OffsetDateTime;
import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class DeviceManagementService {

    private final DeviceRepository deviceRepository;
    private final Clock clock;

    public DeviceManagementService(DeviceRepository deviceRepository, Clock clock) {
        this.deviceRepository = deviceRepository;
        this.clock = clock;
    }

    @Transactional
    public Device register(String classroomId, Device device) {
        deviceRepository.create(classroomId, device, nowUtc());
        return device;
    }

    public List<Device> devicesByClassroom(String classroomId) {
        return deviceRepository.findByClassroomId(classroomId);
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }
}
