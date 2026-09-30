package com.galtek.classroom.activity;

import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.operations.ErrorCode;
import java.time.Clock;
import java.time.LocalDate;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled",
        havingValue = "true", matchIfMissing = true)
public class DeviceActivityService {
    private final MasterAccessGuard accessGuard;
    private final DeviceRepository deviceRepository;
    private final DeviceActivityRepository repository;
    private final Clock clock;

    public DeviceActivityService(
            MasterAccessGuard accessGuard,
            DeviceRepository deviceRepository,
            DeviceActivityRepository repository,
            Clock clock) {
        this.accessGuard = accessGuard;
        this.deviceRepository = deviceRepository;
        this.repository = repository;
        this.clock = clock;
    }

    public List<DeviceActivityEvent> read(String classroomId, String deviceId, String date, int limit) {
        accessGuard.requireAuthorized();
        boolean belongs = deviceRepository.findByClassroomId(required(classroomId, "classroomId")).stream()
                .anyMatch(device -> device.deviceId().equals(required(deviceId, "deviceId")));
        if (!belongs) {
            throw new ApiException(HttpStatus.NOT_FOUND, ErrorCode.DEVICE_NOT_FOUND,
                    "Device does not belong to classroom.");
        }
        int boundedLimit = Math.max(1, Math.min(limit, 50));
        LocalDate requested = date == null || date.isBlank() || "today".equalsIgnoreCase(date)
                ? LocalDate.ofInstant(clock.instant(), ZoneOffset.UTC)
                : LocalDate.parse(date);
        OffsetDateTime from = requested.atStartOfDay().atOffset(ZoneOffset.UTC);
        return repository.find(classroomId.trim(), deviceId.trim(), from, from.plusDays(1), boundedLimit);
    }

    private static String required(String value, String field) {
        if (value == null || value.isBlank()) {
            throw new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, field + " is required.");
        }
        return value.trim();
    }
}
