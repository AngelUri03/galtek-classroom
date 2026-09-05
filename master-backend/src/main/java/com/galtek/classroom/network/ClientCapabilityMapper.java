package com.galtek.classroom.network;

import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.network.v1.ClientHello;
import com.galtek.classroom.network.v1.NetworkCapability;
import java.util.EnumSet;
import java.util.Set;

public final class ClientCapabilityMapper {

    private ClientCapabilityMapper() {
    }

    public static Set<DeviceCapability> fromHello(ClientHello hello) {
        if (hello == null || hello.getCapabilitiesCount() == 0) {
            return Set.of();
        }

        EnumSet<DeviceCapability> capabilities = EnumSet.noneOf(DeviceCapability.class);
        for (NetworkCapability capability : hello.getCapabilitiesList()) {
            DeviceCapability mapped = map(capability);
            if (mapped != null) {
                capabilities.add(mapped);
            }
        }

        return Set.copyOf(capabilities);
    }

    private static DeviceCapability map(NetworkCapability capability) {
        return switch (capability) {
            case NETWORK_CAPABILITY_HEARTBEAT_V1 -> DeviceCapability.HEARTBEAT_V1;
            case NETWORK_CAPABILITY_OPERATION_FRAMEWORK_V1 -> DeviceCapability.OPERATION_FRAMEWORK_V1;
            case NETWORK_CAPABILITY_SESSION_AGENT_AVAILABLE -> DeviceCapability.SESSION_AGENT_AVAILABLE;
            case NETWORK_CAPABILITY_POWER_CONTROL_V1 -> DeviceCapability.POWER_CONTROL_V1;
            case NETWORK_CAPABILITY_OPEN_APPLICATION_V1 -> DeviceCapability.OPEN_APPLICATION_V1;
            case NETWORK_CAPABILITY_OPEN_URL_V1 -> DeviceCapability.OPEN_URL_V1;
            case NETWORK_CAPABILITY_BROWSER_NAVIGATION_POLICY_V1 -> DeviceCapability.BROWSER_NAVIGATION_POLICY_V1;
            case NETWORK_CAPABILITY_BROWSER_DOWNLOAD_POLICY_V1 -> DeviceCapability.BROWSER_DOWNLOAD_POLICY_V1;
            case NETWORK_CAPABILITY_INPUT_CONTROL_V1 -> DeviceCapability.INPUT_CONTROL_V1;
            case NETWORK_CAPABILITY_WINDOWS_SESSION_STATE_V1 -> DeviceCapability.WINDOWS_SESSION_STATE_V1;
            case NETWORK_CAPABILITY_WINDOWS_SESSION_LOGON_V1 -> DeviceCapability.WINDOWS_SESSION_LOGON_V1;
            case NETWORK_CAPABILITY_WINDOWS_SESSION_LOGOFF_V1 -> DeviceCapability.WINDOWS_SESSION_LOGOFF_V1;
            case NETWORK_CAPABILITY_MANAGED_CREDENTIAL_PROVISIONING_V1 ->
                    DeviceCapability.MANAGED_CREDENTIAL_PROVISIONING_V1;
            case NETWORK_CAPABILITY_UNSPECIFIED, UNRECOGNIZED -> null;
        };
    }
}
