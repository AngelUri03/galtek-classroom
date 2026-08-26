package com.galtek.classroom.localagent;

public interface LocalAgentClient {

    DeviceStatusResponse getDeviceStatus();

    MachineCodeResponse getMachineCode();
}
