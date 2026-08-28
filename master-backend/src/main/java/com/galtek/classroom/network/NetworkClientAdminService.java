package com.galtek.classroom.network;

import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.network.NetworkDtos.DeviceRegistrationResponse;
import com.galtek.classroom.network.NetworkDtos.NetworkClientResponse;
import com.galtek.classroom.network.NetworkDtos.RegisterDeviceRequest;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.time.Clock;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import java.util.UUID;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class NetworkClientAdminService {

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterPairingService pairingService;
    private final DeviceNetworkBindingRepository bindingRepository;
    private final DeviceRepository deviceRepository;
    private final MasterAdminRepository adminRepository;
    private final ClientConnectionRegistry connectionRegistry;
    private final Clock clock;

    public NetworkClientAdminService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterPairingService pairingService,
            DeviceNetworkBindingRepository bindingRepository,
            DeviceRepository deviceRepository,
            MasterAdminRepository adminRepository,
            ClientConnectionRegistry connectionRegistry,
            Clock clock) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.pairingService = pairingService;
        this.bindingRepository = bindingRepository;
        this.deviceRepository = deviceRepository;
        this.adminRepository = adminRepository;
        this.connectionRegistry = connectionRegistry;
        this.clock = clock;
    }

    public List<NetworkClientResponse> clients() {
        requireAuthorizedAndStorage();

        Map<UUID, RegisteredNetworkDevice> bindingsByIdentity = bindingRepository.findAllCurrent().stream()
                .collect(Collectors.toMap(
                        RegisteredNetworkDevice::networkIdentityId,
                        Function.identity(),
                        (left, right) -> left));
        Map<UUID, ClientConnectionSnapshot> snapshotsByIdentity = connectionRegistry.snapshots().stream()
                .collect(Collectors.toMap(
                        ClientConnectionSnapshot::clientNetworkIdentityId,
                        Function.identity(),
                        (left, right) -> left));

        return pairingService.knownClients().stream()
                .filter(client -> client.status() == PairingStatus.PAIRED
                        || client.status() == PairingStatus.REVOKED)
                .map(client -> responseFor(
                        client,
                        bindingsByIdentity.get(client.clientNetworkIdentityId()),
                        snapshotsByIdentity.get(client.clientNetworkIdentityId())))
                .sorted(Comparator
                        .comparing(NetworkClientResponse::displayName, Comparator.nullsLast(String::compareTo))
                        .thenComparing(NetworkClientResponse::networkIdentityId))
                .toList();
    }

    @Transactional
    public DeviceRegistrationResponse registerDevice(
            String classroomId,
            RegisterDeviceRequest request) {
        requireAuthorizedAndStorage();

        ClassroomResponse classroom = adminRepository.findClassroom(required(classroomId, "classroomId"))
                .orElseThrow(() -> notFound(ErrorCode.CLASSROOM_NOT_FOUND, "Classroom was not found."));
        if (!classroom.active()) {
            throw conflict(ErrorCode.CLASSROOM_NOT_FOUND, "Classroom is archived.");
        }

        UUID networkIdentityId = requiredUuid(
                request == null ? null : request.networkIdentityId(),
                "networkIdentityId");
        String displayName = required(request == null ? null : request.displayName(), "displayName");
        KnownMasterClient client = pairedClientOrThrow(networkIdentityId);

        bindingRepository.findCurrentByNetworkIdentityId(networkIdentityId)
                .ifPresent(binding -> {
                    throw conflict(
                            ErrorCode.NETWORK_IDENTITY_ALREADY_REGISTERED,
                            "Network Identity is already registered to a Device.");
                });
        bindingRepository.findCurrentByInstallationId(client.clientInstallationId())
                .ifPresent(binding -> {
                    throw conflict(
                            ErrorCode.DEVICE_ALREADY_REGISTERED,
                            "Client installation is already registered to a Device.");
                });
        if (bindingRepository.activeDeviceExistsForInstallation(client.clientInstallationId())) {
            throw conflict(
                    ErrorCode.DEVICE_ALREADY_REGISTERED,
                    "Client installation already belongs to a Device.");
        }

        ClientConnectionSnapshot live = connectionRegistry.find(networkIdentityId).orElse(null);
        Set<DeviceCapability> capabilities = live == null ? Set.of() : live.capabilities();
        OffsetDateTime registeredAtUtc = nowUtc();
        OffsetDateTime lastConnectedAtUtc = live == null ? null : toOffset(live.connectedAtUtc());
        String deviceId = UUID.randomUUID().toString();
        Device device = new Device(
                deviceId,
                client.clientInstallationId().toString(),
                displayName,
                hostnameFor(live, displayName),
                DeviceStatus.OFFLINE,
                lastConnectedAtUtc == null ? registeredAtUtc : lastConnectedAtUtc,
                capabilities,
                null);
        deviceRepository.create(classroom.classroomId(), device, registeredAtUtc);

        DeviceNetworkBinding binding = new DeviceNetworkBinding(
                UUID.randomUUID().toString(),
                deviceId,
                client.clientInstallationId(),
                client.clientNetworkIdentityId(),
                client.clientPublicKeyFingerprint(),
                live == null ? null : live.agentVersion(),
                capabilities,
                registeredAtUtc,
                lastConnectedAtUtc,
                true,
                0);
        bindingRepository.create(binding, registeredAtUtc);

        RegisteredNetworkDevice registeredDevice = bindingRepository.findCurrentByNetworkIdentityId(networkIdentityId)
                .orElseThrow(() -> conflict(
                        ErrorCode.DEVICE_NOT_REGISTERED,
                        "Device registration could not be read after creation."));
        connectionRegistry.attachRegistration(registeredDevice);

        return new DeviceRegistrationResponse(
                registeredDevice.deviceId(),
                registeredDevice.classroomId(),
                registeredDevice.networkIdentityId().toString(),
                registeredDevice.installationId().toString(),
                registeredDevice.displayName(),
                registeredDevice.agentVersion(),
                capabilityNames(registeredDevice.capabilities()),
                registeredDevice.registeredAtUtc(),
                registeredDevice.lastConnectedAtUtc());
    }

    private NetworkClientResponse responseFor(
            KnownMasterClient client,
            RegisteredNetworkDevice binding,
            ClientConnectionSnapshot snapshot) {
        boolean registered = binding != null;
        String connectionStatus = snapshot == null
                ? DeviceStatus.OFFLINE.name()
                : snapshot.status().name();
        Set<DeviceCapability> capabilities = snapshot != null && !snapshot.capabilities().isEmpty()
                ? snapshot.capabilities()
                : (binding == null ? Set.of() : binding.capabilities());

        return new NetworkClientResponse(
                client.clientNetworkIdentityId().toString(),
                client.status().name(),
                connectionStatus,
                registrationStatus(client.status(), registered, snapshot),
                registered,
                binding == null ? null : binding.deviceId(),
                binding == null ? null : binding.classroomId(),
                displayNameFor(client, binding, snapshot),
                agentVersionFor(binding, snapshot),
                capabilityNames(capabilities),
                lastSeenFor(binding, snapshot),
                lastConnectedFor(binding, snapshot));
    }

    private KnownMasterClient pairedClientOrThrow(UUID networkIdentityId) {
        KnownMasterClient client = pairingService.knownClient(networkIdentityId)
                .orElseThrow(() -> new ApiException(
                        HttpStatus.FORBIDDEN,
                        ErrorCode.MASTER_NOT_PAIRED,
                        "Client is not paired with this Master."));
        if (client.status() == PairingStatus.REVOKED) {
            throw new ApiException(
                    HttpStatus.FORBIDDEN,
                    ErrorCode.CLIENT_REVOKED,
                    "Client pairing has been revoked.");
        }
        if (client.status() != PairingStatus.PAIRED) {
            throw new ApiException(
                    HttpStatus.FORBIDDEN,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client is not paired with this Master.");
        }
        return client;
    }

    private MasterAuthorizationResponse requireAuthorizedAndStorage() {
        MasterAuthorizationResponse authorization = masterAccessGuard.requireAuthorized();
        MasterStorageHealth health = storageState.health();
        if (health.status() != MasterStorageStatus.READY) {
            String code = health.errorCode() == null
                    ? ErrorCode.MASTER_DATABASE_UNAVAILABLE.name()
                    : health.errorCode();
            throw new ApiException(
                    HttpStatus.SERVICE_UNAVAILABLE,
                    code,
                    "Master storage is unavailable.");
        }

        return authorization;
    }

    private String displayNameFor(
            KnownMasterClient client,
            RegisteredNetworkDevice binding,
            ClientConnectionSnapshot snapshot) {
        if (binding != null) {
            return binding.displayName();
        }
        if (snapshot != null && snapshot.displayName() != null && !snapshot.displayName().isBlank()) {
            return snapshot.displayName();
        }
        return client.clientNetworkIdentityId().toString();
    }

    private String agentVersionFor(
            RegisteredNetworkDevice binding,
            ClientConnectionSnapshot snapshot) {
        if (snapshot != null && snapshot.agentVersion() != null && !snapshot.agentVersion().isBlank()) {
            return snapshot.agentVersion();
        }
        return binding == null ? null : binding.agentVersion();
    }

    private OffsetDateTime lastSeenFor(
            RegisteredNetworkDevice binding,
            ClientConnectionSnapshot snapshot) {
        if (snapshot == null) {
            return binding == null ? null : binding.lastConnectedAtUtc();
        }
        Instant signal = snapshot.lastHeartbeatUtc() == null
                ? (snapshot.disconnectedAtUtc() == null ? snapshot.connectedAtUtc() : snapshot.disconnectedAtUtc())
                : snapshot.lastHeartbeatUtc();
        return toOffset(signal);
    }

    private OffsetDateTime lastConnectedFor(
            RegisteredNetworkDevice binding,
            ClientConnectionSnapshot snapshot) {
        if (binding != null && binding.lastConnectedAtUtc() != null) {
            return binding.lastConnectedAtUtc();
        }
        return snapshot == null ? null : toOffset(snapshot.connectedAtUtc());
    }

    private String registrationStatus(
            PairingStatus pairingStatus,
            boolean registered,
            ClientConnectionSnapshot snapshot) {
        if (pairingStatus == PairingStatus.REVOKED) {
            return "REVOKED";
        }
        if (registered) {
            return "REGISTERED";
        }
        if (snapshot != null && snapshot.status() == DeviceStatus.ONLINE) {
            return "AVAILABLE_FOR_REGISTRATION";
        }
        return "PAIRED";
    }

    private static String hostnameFor(ClientConnectionSnapshot live, String displayName) {
        if (live != null && live.hostname() != null && !live.hostname().isBlank()) {
            return live.hostname();
        }
        return displayName;
    }

    private Set<String> capabilityNames(Set<DeviceCapability> capabilities) {
        if (capabilities == null || capabilities.isEmpty()) {
            return Set.of();
        }
        return capabilities.stream()
                .map(Enum::name)
                .collect(Collectors.toCollection(TreeSet::new));
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.ofInstant(clock.instant(), ZoneOffset.UTC);
    }

    private OffsetDateTime toOffset(Instant instant) {
        return instant == null ? null : OffsetDateTime.ofInstant(instant, ZoneOffset.UTC);
    }

    private UUID requiredUuid(String value, String fieldName) {
        String clean = required(value, fieldName);
        try {
            return UUID.fromString(clean);
        } catch (IllegalArgumentException exception) {
            throw validation(fieldName + " must be a valid UUID.");
        }
    }

    private String required(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw validation(fieldName + " is required.");
        }
        return value.trim();
    }

    private ApiException notFound(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.NOT_FOUND, errorCode, message);
    }

    private ApiException conflict(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.CONFLICT, errorCode, message);
    }

    private ApiException validation(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, message);
    }
}
