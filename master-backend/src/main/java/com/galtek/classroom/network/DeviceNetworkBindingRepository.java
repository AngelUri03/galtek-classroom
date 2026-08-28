package com.galtek.classroom.network;

import com.galtek.classroom.device.DeviceCapability;
import java.time.OffsetDateTime;
import java.util.Collection;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.UUID;

public interface DeviceNetworkBindingRepository {

    void create(DeviceNetworkBinding binding, OffsetDateTime nowUtc);

    Optional<RegisteredNetworkDevice> findCurrentByNetworkIdentityId(UUID networkIdentityId);

    Optional<RegisteredNetworkDevice> findCurrentByInstallationId(UUID installationId);

    Optional<RegisteredNetworkDevice> findCurrentByDeviceId(String deviceId);

    List<RegisteredNetworkDevice> findCurrentByDeviceIds(Collection<String> deviceIds);

    List<RegisteredNetworkDevice> findAllCurrent();

    boolean activeDeviceExistsForInstallation(UUID installationId);

    void recordConnection(
            UUID networkIdentityId,
            String agentVersion,
            Set<DeviceCapability> capabilities,
            OffsetDateTime connectedAtUtc,
            OffsetDateTime nowUtc);
}
