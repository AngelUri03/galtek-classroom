package com.galtek.classroom.network;

import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.network.v1.ClientHello;
import java.time.Clock;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.Set;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class PersistentNetworkClientConnectionService implements NetworkClientConnectionService {

    private final DeviceNetworkBindingRepository bindingRepository;
    private final Clock clock;

    public PersistentNetworkClientConnectionService(
            DeviceNetworkBindingRepository bindingRepository,
            Clock clock) {
        this.bindingRepository = bindingRepository;
        this.clock = clock;
    }

    @Override
    public RegisteredNetworkDevice recordAcceptedHello(
            ClientNetworkIdentityDescriptor descriptor,
            ClientHello hello) {
        return bindingRepository.findCurrentByNetworkIdentityId(descriptor.clientNetworkIdentityId())
                .filter(binding -> binding.installationId().equals(descriptor.clientInstallationId()))
                .filter(binding -> binding.publicKeyFingerprint().equals(descriptor.publicKeyFingerprint()))
                .map(binding -> {
                    Set<DeviceCapability> capabilities = ClientCapabilityMapper.fromHello(hello);
                    OffsetDateTime connectedAtUtc = nowUtc();
                    String agentVersion = emptyToNull(hello.getAgentVersion());
                    bindingRepository.recordConnection(
                            descriptor.clientNetworkIdentityId(),
                            agentVersion,
                            capabilities,
                            connectedAtUtc,
                            connectedAtUtc);
                    return new RegisteredNetworkDevice(
                            binding.bindingId(),
                            binding.deviceId(),
                            binding.classroomId(),
                            binding.installationId(),
                            binding.networkIdentityId(),
                            binding.publicKeyFingerprint(),
                            binding.displayName(),
                            binding.hostname(),
                            agentVersion,
                            capabilities,
                            binding.registeredAtUtc(),
                            connectedAtUtc,
                            binding.active(),
                            binding.version() + 1);
                })
                .orElse(null);
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.ofInstant(clock.instant(), ZoneOffset.UTC);
    }

    private static String emptyToNull(String value) {
        return value == null || value.isBlank() ? null : value.trim();
    }
}
