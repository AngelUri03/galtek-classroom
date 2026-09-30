package com.galtek.classroom.operations;

import com.galtek.classroom.master.MasterAccessGuard;
import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/device-operations")
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled",
        havingValue = "true", matchIfMissing = true)
public class DeviceMutationController {
    private final DeviceMutationCoordinator coordinator;
    private final MasterAccessGuard accessGuard;

    public DeviceMutationController(DeviceMutationCoordinator coordinator, MasterAccessGuard accessGuard) {
        this.coordinator = coordinator;
        this.accessGuard = accessGuard;
    }

    @GetMapping
    public DeviceMutationResponse current(@RequestParam(name = "deviceId") List<String> deviceIds) {
        accessGuard.requireAuthorized();
        return new DeviceMutationResponse(coordinator.find(deviceIds));
    }

    public record DeviceMutationResponse(List<DeviceMutationCoordinator.DeviceMutationState> devices) {
    }
}
