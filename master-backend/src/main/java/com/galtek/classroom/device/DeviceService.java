package com.galtek.classroom.device;

import com.galtek.classroom.localagent.DeviceStatusResponse;
import com.galtek.classroom.localagent.LocalAgentClient;
import com.galtek.classroom.localagent.MachineCodeResponse;
import org.springframework.stereotype.Service;

@Service
public class DeviceService {

    private final LocalAgentClient localAgentClient;

    public DeviceService(LocalAgentClient localAgentClient) {
        this.localAgentClient = localAgentClient;
    }

    public DeviceStatusResponse currentStatus() {
        return localAgentClient.getDeviceStatus();
    }

    public MachineCodeResponse machineCode() {
        return localAgentClient.getMachineCode();
    }
}
