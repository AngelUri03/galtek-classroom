package com.galtek.classroom.windows;

import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientConnectionSnapshot;
import com.galtek.classroom.network.DeviceNetworkBindingRepository;
import com.galtek.classroom.network.KnownMasterClient;
import com.galtek.classroom.network.MasterPairingService;
import com.galtek.classroom.network.MasterRemoteOperationGateway;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.PairingStatus;
import com.galtek.classroom.network.RegisteredNetworkDevice;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import com.galtek.classroom.windows.WindowsSessionStateDtos.WindowsSessionStateBatchResponse;
import com.galtek.classroom.windows.WindowsSessionStateDtos.WindowsSessionStateTargetResponse;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class WindowsSessionStateReadService {

    private static final Set<String> REQUEST_FIELDS = Set.of("targetDeviceIds");
    private static final int MAX_TARGET_DEVICES = 100;

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterAdminRepository adminRepository;
    private final DeviceRepository deviceRepository;
    private final DeviceNetworkBindingRepository bindingRepository;
    private final MasterPairingService pairingService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway remoteOperationGateway;

    public WindowsSessionStateReadService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterAdminRepository adminRepository,
            DeviceRepository deviceRepository,
            DeviceNetworkBindingRepository bindingRepository,
            MasterPairingService pairingService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway remoteOperationGateway) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.adminRepository = adminRepository;
        this.deviceRepository = deviceRepository;
        this.bindingRepository = bindingRepository;
        this.pairingService = pairingService;
        this.connectionRegistry = connectionRegistry;
        this.remoteOperationGateway = remoteOperationGateway;
    }

    public WindowsSessionStateBatchResponse read(String classroomId, Map<String, Object> request) {
        requireAuthorizedAndStorage();
        List<String> targetDeviceIds = targetDeviceIdsFrom(request);
        ClassroomResponse classroom = classroomOr404(classroomId);
        List<TargetPlan> plans = targetPlans(classroom.classroomId(), targetDeviceIds);
        Map<String, DispatchHandle> dispatched = new LinkedHashMap<>();
        Map<String, WindowsSessionStateTargetResponse> responsesByDeviceId = new LinkedHashMap<>();

        for (TargetPlan plan : plans) {
            if (!plan.ready()) {
                responsesByDeviceId.put(plan.deviceId(), WindowsSessionStateTargetResponse.failed(
                        plan.deviceId(),
                        plan.errorCode(),
                        plan.message(),
                        plan.online()));
                continue;
            }

            remoteOperationGateway.getWindowsSessionState(
                            plan.snapshot(),
                            UUID.randomUUID().toString(),
                            plan.deviceId())
                    .ifPresentOrElse(
                            handle -> dispatched.put(plan.deviceId(), handle),
                            () -> responsesByDeviceId.put(plan.deviceId(), WindowsSessionStateTargetResponse.failed(
                                    plan.deviceId(),
                                    ErrorCode.DEVICE_OFFLINE,
                                    "Device is offline.",
                                    false)));
        }

        long deadlineNanos = System.nanoTime() + remoteOperationGateway.resultTimeout().toNanos();

        for (TargetPlan plan : plans) {
            DispatchHandle handle = dispatched.get(plan.deviceId());
            if (handle == null) {
                continue;
            }

            RemoteOperationOutcome outcome = awaitOutcome(handle, deadlineNanos);
            if (outcome.status() == TargetExecutionStatus.SUCCESS && outcome.windowsSessionState() != null) {
                responsesByDeviceId.put(plan.deviceId(), WindowsSessionStateTargetResponse.success(
                        plan.deviceId(),
                        mapSessionState(outcome.windowsSessionState())));
            } else {
                responsesByDeviceId.put(plan.deviceId(), WindowsSessionStateTargetResponse.failed(
                        plan.deviceId(),
                        outcome.errorCode() == null ? ErrorCode.WINDOWS_SESSION_UNKNOWN : outcome.errorCode(),
                        outcome.message() == null ? "Windows session state is unavailable." : outcome.message(),
                        true));
            }
        }

        List<WindowsSessionStateTargetResponse> targets = plans.stream()
                .map(plan -> responsesByDeviceId.get(plan.deviceId()))
                .toList();
        return new WindowsSessionStateBatchResponse(classroom.classroomId(), targets.size(), targets);
    }

    private List<TargetPlan> targetPlans(String classroomId, List<String> targetDeviceIds) {
        TargetContext context = targetContext(classroomId, targetDeviceIds);
        List<TargetPlan> plans = new ArrayList<>(targetDeviceIds.size());
        for (String deviceId : targetDeviceIds) {
            plans.add(preflight(deviceId, context));
        }
        return plans;
    }

    private TargetContext targetContext(String classroomId, List<String> targetDeviceIds) {
        Map<String, Device> devices = deviceRepository.findByClassroomId(classroomId).stream()
                .collect(Collectors.toMap(
                        Device::deviceId,
                        Function.identity(),
                        (left, right) -> left,
                        LinkedHashMap::new));
        Map<String, RegisteredNetworkDevice> bindings = bindingRepository.findCurrentByDeviceIds(targetDeviceIds).stream()
                .collect(Collectors.toMap(
                        RegisteredNetworkDevice::deviceId,
                        Function.identity(),
                        (left, right) -> left,
                        LinkedHashMap::new));
        Map<String, ClientConnectionSnapshot> snapshots = connectionRegistry.snapshotsByDeviceId();
        Map<UUID, KnownMasterClient> knownClients = pairingService.knownClients().stream()
                .collect(Collectors.toMap(
                        KnownMasterClient::clientNetworkIdentityId,
                        Function.identity(),
                        (left, right) -> left,
                        LinkedHashMap::new));
        return new TargetContext(devices, bindings, snapshots, knownClients);
    }

    private TargetPlan preflight(String deviceId, TargetContext context) {
        Device device = context.devices().get(deviceId);
        if (device == null) {
            return TargetPlan.blocked(
                    deviceId,
                    ErrorCode.DEVICE_NOT_FOUND,
                    "Device does not belong to classroom.",
                    false);
        }

        RegisteredNetworkDevice binding = context.bindings().get(deviceId);
        if (binding == null) {
            return TargetPlan.blocked(
                    deviceId,
                    ErrorCode.DEVICE_NOT_REGISTERED,
                    "Device is not registered for network operations.",
                    false);
        }

        TargetPlan trustBlocked = trustBlock(deviceId, binding, context.knownClients());
        if (trustBlocked != null) {
            return trustBlocked;
        }

        ClientConnectionSnapshot snapshot = context.snapshots().get(deviceId);
        if (!authenticatedOnline(snapshot, binding)) {
            return TargetPlan.blocked(
                    deviceId,
                    ErrorCode.DEVICE_OFFLINE,
                    "Device is offline.",
                    false);
        }

        if (!snapshot.capabilities().contains(DeviceCapability.WINDOWS_SESSION_STATE_V1)) {
            return TargetPlan.blocked(
                    deviceId,
                    ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    "Device does not announce WINDOWS_SESSION_STATE_V1.",
                    true);
        }

        return TargetPlan.ready(deviceId, snapshot);
    }

    private TargetPlan trustBlock(
            String deviceId,
            RegisteredNetworkDevice binding,
            Map<UUID, KnownMasterClient> knownClients) {
        KnownMasterClient client = knownClients.get(binding.networkIdentityId());
        if (client != null && client.status() == PairingStatus.REVOKED) {
            return TargetPlan.blocked(
                    deviceId,
                    ErrorCode.CLIENT_REVOKED,
                    "Client pairing has been revoked.",
                    false);
        }

        if (client == null || client.status() != PairingStatus.PAIRED
                || !client.clientInstallationId().equals(binding.installationId())
                || !client.clientPublicKeyFingerprint().equals(binding.publicKeyFingerprint())) {
            return TargetPlan.blocked(
                    deviceId,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client is not paired with this Master.",
                    false);
        }

        return null;
    }

    private boolean authenticatedOnline(
            ClientConnectionSnapshot snapshot,
            RegisteredNetworkDevice binding) {
        return snapshot != null
                && snapshot.registered()
                && snapshot.status() == DeviceStatus.ONLINE
                && snapshot.clientNetworkIdentityId().equals(binding.networkIdentityId())
                && snapshot.clientInstallationId().equals(binding.installationId())
                && snapshot.deviceId() != null
                && snapshot.deviceId().equals(binding.deviceId())
                && snapshot.connectionId() != null;
    }

    private RemoteOperationOutcome awaitOutcome(DispatchHandle handle, long deadlineNanos) {
        long remainingNanos = deadlineNanos - System.nanoTime();
        if (remainingNanos <= 0) {
            return remoteOperationGateway.timeout(handle);
        }

        try {
            return handle.completion().get(remainingNanos, TimeUnit.NANOSECONDS);
        } catch (TimeoutException exception) {
            return remoteOperationGateway.timeout(handle);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return remoteOperationGateway.timeout(handle);
        } catch (ExecutionException exception) {
            return remoteOperationGateway.timeout(handle);
        }
    }

    private WindowsSessionState mapSessionState(
            com.galtek.classroom.network.v1.WindowsSessionState sessionState) {
        return switch (sessionState) {
            case WINDOWS_SESSION_STATE_NO_SESSION -> WindowsSessionState.NO_SESSION;
            case WINDOWS_SESSION_STATE_PRIMARY_ACTIVE -> WindowsSessionState.PRIMARY_ACTIVE;
            case WINDOWS_SESSION_STATE_SECONDARY_ACTIVE -> WindowsSessionState.SECONDARY_ACTIVE;
            case WINDOWS_SESSION_STATE_OTHER_SESSION_ACTIVE -> WindowsSessionState.OTHER_SESSION_ACTIVE;
            case WINDOWS_SESSION_STATE_UNKNOWN -> WindowsSessionState.UNKNOWN;
            case WINDOWS_SESSION_STATE_UNSPECIFIED, UNRECOGNIZED -> WindowsSessionState.UNKNOWN;
        };
    }

    private List<String> targetDeviceIdsFrom(Map<String, Object> request) {
        requireKnownBody(request);
        Object value = request.get("targetDeviceIds");
        if (!(value instanceof List<?> rawTargets) || rawTargets.isEmpty()) {
            throw validation("targetDeviceIds is required and must not be empty.");
        }
        if (rawTargets.size() > MAX_TARGET_DEVICES) {
            throw validation("targetDeviceIds exceeds the maximum target count.");
        }

        List<String> targetDeviceIds = new ArrayList<>(rawTargets.size());
        LinkedHashSet<String> unique = new LinkedHashSet<>();
        for (Object rawTarget : rawTargets) {
            if (!(rawTarget instanceof String targetId) || targetId.isBlank()) {
                throw validation("targetDeviceIds must contain non-empty strings.");
            }

            String cleanTargetId = targetId.trim();
            if (!unique.add(cleanTargetId)) {
                throw validation("targetDeviceIds must not contain duplicates.");
            }
            targetDeviceIds.add(cleanTargetId);
        }

        return targetDeviceIds;
    }

    private void requireKnownBody(Map<String, Object> request) {
        if (request == null) {
            throw validation("Request body is required.");
        }
        for (String field : request.keySet()) {
            if (!REQUEST_FIELDS.contains(field)) {
                throw validation("Request contains unsupported field: " + field + ".");
            }
        }
    }

    private ClassroomResponse classroomOr404(String classroomId) {
        String cleanId = required(classroomId, "classroomId");
        return adminRepository.findClassroom(cleanId)
                .orElseThrow(() -> new ApiException(
                        HttpStatus.NOT_FOUND,
                        ErrorCode.CLASSROOM_NOT_FOUND,
                        "Classroom was not found."));
    }

    private void requireAuthorizedAndStorage() {
        masterAccessGuard.requireAuthorized();
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
    }

    private String required(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw validation(fieldName + " is required.");
        }
        return value.trim();
    }

    private ApiException validation(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, message);
    }

    private record TargetContext(
            Map<String, Device> devices,
            Map<String, RegisteredNetworkDevice> bindings,
            Map<String, ClientConnectionSnapshot> snapshots,
            Map<UUID, KnownMasterClient> knownClients) {
    }

    private record TargetPlan(
            String deviceId,
            ClientConnectionSnapshot snapshot,
            ErrorCode errorCode,
            String message,
            boolean online) {

        static TargetPlan ready(String deviceId, ClientConnectionSnapshot snapshot) {
            return new TargetPlan(deviceId, snapshot, null, null, true);
        }

        static TargetPlan blocked(
                String deviceId,
                ErrorCode errorCode,
                String message,
                boolean online) {
            return new TargetPlan(deviceId, null, errorCode, message, online);
        }

        boolean ready() {
            return errorCode == null;
        }
    }
}
