package com.galtek.classroom.device;

import com.galtek.classroom.localagent.DeviceStatusResponse;
import com.galtek.classroom.localagent.MachineCodeResponse;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/device")
public class DeviceController {

    private final DeviceService deviceService;

    public DeviceController(DeviceService deviceService) {
        this.deviceService = deviceService;
    }

    @GetMapping("/status")
    public DeviceStatusResponse status() {
        return deviceService.currentStatus();
    }

    @GetMapping("/machine-code")
    public MachineCodeResponse machineCode() {
        return deviceService.machineCode();
    }
}
