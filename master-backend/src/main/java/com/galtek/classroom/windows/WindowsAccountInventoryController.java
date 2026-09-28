package com.galtek.classroom.windows;

import com.galtek.classroom.windows.WindowsAccountInventoryDtos.WindowsAccountInventoryResponse;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api")
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class WindowsAccountInventoryController {

    private final WindowsAccountInventoryReadService service;

    public WindowsAccountInventoryController(WindowsAccountInventoryReadService service) {
        this.service = service;
    }

    @GetMapping("/classrooms/{classroomId}/devices/{deviceId}/windows-accounts")
    public WindowsAccountInventoryResponse read(
            @PathVariable String classroomId,
            @PathVariable String deviceId) {
        return service.read(classroomId, deviceId);
    }
}
