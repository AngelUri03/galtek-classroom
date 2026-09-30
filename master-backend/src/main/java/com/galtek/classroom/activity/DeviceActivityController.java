package com.galtek.classroom.activity;

import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api")
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled",
        havingValue = "true", matchIfMissing = true)
public class DeviceActivityController {
    private final DeviceActivityService service;

    public DeviceActivityController(DeviceActivityService service) {
        this.service = service;
    }

    @GetMapping("/classrooms/{classroomId}/devices/{deviceId}/activity")
    public ActivityResponse read(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @RequestParam(defaultValue = "today") String date,
            @RequestParam(defaultValue = "50") int limit) {
        return new ActivityResponse(service.read(classroomId, deviceId, date, limit));
    }

    public record ActivityResponse(List<DeviceActivityEvent> events) {}
}
