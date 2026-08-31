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
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.NetworkDtos.PowerControlBatchResponse;
import com.galtek.classroom.network.NetworkDtos.PowerControlTargetResponse;
import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchOperationService;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationPayload;
import com.galtek.classroom.operations.OperationTarget;
import com.galtek.classroom.operations.OperationTargetType;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.time.Clock;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class PowerControlDispatchService {

    private static final Set<String> REQUEST_FIELDS = Set.of("type", "targetDeviceIds");
    private static final String REQUESTED_BY = "LOCAL_MASTER";

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterAdminRepository adminRepository;
    private final DeviceRepository deviceRepository;
    private final DeviceNetworkBindingRepository bindingRepository;
    private final MasterPairingService pairingService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway remoteOperationGateway;
    private final BatchOperationService batchOperationService;
    private final Clock clock;

    public PowerControlDispatchService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterAdminRepository adminRepository,
            DeviceRepository deviceRepository,
            DeviceNetworkBindingRepository bindingRepository,
            MasterPairingService pairingService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway remoteOperationGateway,
            BatchOperationService batchOperationService,
            Clock clock) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.adminRepository = adminRepository;
        this.deviceRepository = deviceRepository;
        this.bindingRepository = bindingRepository;
        this.pairingService = pairingService;
        this.connectionRegistry = connectionRegistry;
        this.remoteOperationGateway = remoteOperationGateway;
        this.batchOperationService = batchOperationService;
        this.clock = clock;
    }

    public PowerControlBatchResponse dispatch(
            String classroomId,
            Map<String, Object> request) {
        requireAuthorizedAndStorage();

        OperationType operationType = operationTypeFrom(request);
        List<String> targetDeviceIds = targetDeviceIdsFrom(request);
        ClassroomResponse classroom = classroomOr404(classroomId);
        String operationId = UUID.randomUUID().toString();
        OffsetDateTime createdAtUtc = nowUtc();

        List<TargetPlan> targetPlans = targetPlans(classroom.classroomId(), targetDeviceIds);
        List<BatchTargetResult> intentTargets = targetPlans.stream()
                .map(TargetPlan::intentResult)
                .toList();
        batchOperationService.create(
                classroom.classroomId(),
                BatchOperation.fromTargets(
                        operationId,
                        operationType,
                        REQUESTED_BY,
                        createdAtUtc,
                        intentTargets),
                OperationPayload.none());

        Map<String, DispatchHandle> dispatched = new LinkedHashMap<>();
        Map<String, BatchTargetResult> finalResultsByDeviceId = new LinkedHashMap<>();
        for (TargetPlan targetPlan : targetPlans) {
            if (!targetPlan.ready()) {
                finalResultsByDeviceId.put(targetPlan.deviceId(), targetPlan.finalFailure());
                continue;
            }

            Optional<DispatchHandle> handle = remoteOperationGateway.dispatch(
                    targetPlan.snapshot(),
                    operationType,
                    operationId,
                    targetPlan.deviceId());
            if (handle.isEmpty()) {
                finalResultsByDeviceId.put(
                        targetPlan.deviceId(),
                        failed(
                                targetPlan.target(),
                                ErrorCode.DEVICE_OFFLINE,
                                "Device is offline.",
                                1));
                continue;
            }

            dispatched.put(targetPlan.deviceId(), handle.get());
        }

        long deadlineNanos = System.nanoTime() + remoteOperationGateway.resultTimeout().toNanos();
        for (TargetPlan targetPlan : targetPlans) {
            DispatchHandle handle = dispatched.get(targetPlan.deviceId());
            if (handle == null) {
                continue;
            }

            RemoteOperationOutcome outcome = awaitOutcome(handle, deadlineNanos);
            finalResultsByDeviceId.put(
                    targetPlan.deviceId(),
                    new BatchTargetResult(
                            targetPlan.target(),
                            outcome.status(),
                            outcome.errorCode(),
                            outcome.message(),
                            1));
        }

        List<BatchTargetResult> finalResults = targetPlans.stream()
                .map(targetPlan -> finalResultsByDeviceId.get(targetPlan.deviceId()))
                .toList();
        BatchOperation completed = BatchOperation.fromTargets(
                operationId,
                operationType,
                REQUESTED_BY,
                createdAtUtc,
                finalResults);
        batchOperationService.replaceResults(completed);
        return responseFrom(completed);
    }

    private List<TargetPlan> targetPlans(String classroomId, List<String> targetDeviceIds) {
        Map<String, Device> devices = devicesById(deviceRepository.findByClassroomId(classroomId));
        Map<String, RegisteredNetworkDevice> bindings = bindingsByDeviceId(
                bindingRepository.findCurrentByDeviceIds(targetDeviceIds));

        List<TargetPlan> plans = new ArrayList<>(targetDeviceIds.size());
        for (String deviceId : targetDeviceIds) {
            Device device = devices.get(deviceId);
            OperationTarget target = new OperationTarget(
                    OperationTargetType.DEVICE,
                    deviceId,
                    device == null ? deviceId : device.displayName());
            if (device == null) {
                plans.add(TargetPlan.blocked(
                        deviceId,
                        target,
                        ErrorCode.DEVICE_NOT_FOUND,
                        "Device does not belong to classroom."));
                continue;
            }

            RegisteredNetworkDevice binding = bindings.get(deviceId);
            if (binding == null) {
                plans.add(TargetPlan.blocked(
                        deviceId,
                        target,
                        ErrorCode.DEVICE_NOT_REGISTERED,
                        "Device is not registered for network operations."));
                continue;
            }

            TargetPlan trustBlocked = trustBlock(deviceId, target, binding);
            if (trustBlocked != null) {
                plans.add(trustBlocked);
                continue;
            }

            ClientConnectionSnapshot snapshot = connectionRegistry.findByDeviceId(deviceId).orElse(null);
            if (!authenticatedOnline(snapshot, binding)) {
                plans.add(TargetPlan.blocked(
                        deviceId,
                        target,
                        ErrorCode.DEVICE_OFFLINE,
                        "Device is offline."));
                continue;
            }

            if (!snapshot.capabilities().contains(DeviceCapability.POWER_CONTROL_V1)) {
                plans.add(TargetPlan.blocked(
                        deviceId,
                        target,
                        ErrorCode.CAPABILITY_NOT_SUPPORTED,
                        "Device does not announce POWER_CONTROL_V1."));
                continue;
            }

            plans.add(TargetPlan.ready(deviceId, target, snapshot));
        }
        return plans;
    }

    private TargetPlan trustBlock(
            String deviceId,
            OperationTarget target,
            RegisteredNetworkDevice binding) {
        KnownMasterClient client = pairingService.knownClient(binding.networkIdentityId()).orElse(null);
        if (client != null && client.status() == PairingStatus.REVOKED) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.CLIENT_REVOKED,
                    "Client pairing has been revoked.");
        }

        if (client == null || client.status() != PairingStatus.PAIRED
                || !client.clientInstallationId().equals(binding.installationId())
                || !client.clientPublicKeyFingerprint().equals(binding.publicKeyFingerprint())) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client is not paired with this Master.");
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

    private PowerControlBatchResponse responseFrom(BatchOperation operation) {
        int successCount = (int) operation.targets().stream()
                .filter(target -> target.status() == TargetExecutionStatus.SUCCESS
                        || target.status() == TargetExecutionStatus.NO_CHANGE)
                .count();
        int failedCount = (int) operation.targets().stream()
                .filter(BatchTargetResult::failed)
                .count();
        return new PowerControlBatchResponse(
                operation.operationId(),
                operation.type().name(),
                operation.status().name(),
                operation.targetCount(),
                successCount,
                failedCount,
                operation.targets().stream()
                        .map(target -> new PowerControlTargetResponse(
                                target.target().targetId(),
                                target.status().name(),
                                target.errorCode() == null ? null : target.errorCode().name(),
                                target.message(),
                                target.attempt()))
                        .toList());
    }

    private OperationType operationTypeFrom(Map<String, Object> request) {
        requireKnownBody(request);
        Object value = request.get("type");
        if (!(value instanceof String rawType) || rawType.isBlank()) {
            throw validation("type is required.");
        }

        try {
            OperationType operationType = OperationType.valueOf(rawType.trim());
            if (operationType == OperationType.SHUTDOWN || operationType == OperationType.RESTART) {
                return operationType;
            }
        } catch (IllegalArgumentException exception) {
            throw validation("type must be SHUTDOWN or RESTART.");
        }

        throw validation("type must be SHUTDOWN or RESTART.");
    }

    private List<String> targetDeviceIdsFrom(Map<String, Object> request) {
        requireKnownBody(request);
        Object value = request.get("targetDeviceIds");
        if (!(value instanceof List<?> rawTargets) || rawTargets.isEmpty()) {
            throw validation("targetDeviceIds is required and must not be empty.");
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
                .orElseThrow(() -> notFound(ErrorCode.CLASSROOM_NOT_FOUND, "Classroom was not found."));
    }

    private Map<String, Device> devicesById(List<Device> devices) {
        Map<String, Device> devicesById = new LinkedHashMap<>();
        for (Device device : devices) {
            devicesById.putIfAbsent(device.deviceId(), device);
        }
        return devicesById;
    }

    private Map<String, RegisteredNetworkDevice> bindingsByDeviceId(List<RegisteredNetworkDevice> bindings) {
        Map<String, RegisteredNetworkDevice> bindingsByDeviceId = new LinkedHashMap<>();
        for (RegisteredNetworkDevice binding : bindings) {
            bindingsByDeviceId.putIfAbsent(binding.deviceId(), binding);
        }
        return bindingsByDeviceId;
    }

    private BatchTargetResult failed(
            OperationTarget target,
            ErrorCode errorCode,
            String message,
            int attempt) {
        return new BatchTargetResult(
                target,
                TargetExecutionStatus.FAILED,
                errorCode,
                message,
                attempt);
    }

    private String required(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw validation(fieldName + " is required.");
        }
        return value.trim();
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

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.ofInstant(clock.instant(), ZoneOffset.UTC);
    }

    private ApiException notFound(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.NOT_FOUND, errorCode, message);
    }

    private ApiException validation(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, message);
    }

    private record TargetPlan(
            String deviceId,
            OperationTarget target,
            ClientConnectionSnapshot snapshot,
            ErrorCode errorCode,
            String message) {

        static TargetPlan ready(
                String deviceId,
                OperationTarget target,
                ClientConnectionSnapshot snapshot) {
            return new TargetPlan(deviceId, target, snapshot, null, null);
        }

        static TargetPlan blocked(
                String deviceId,
                OperationTarget target,
                ErrorCode errorCode,
                String message) {
            return new TargetPlan(deviceId, target, null, errorCode, message);
        }

        boolean ready() {
            return errorCode == null;
        }

        BatchTargetResult intentResult() {
            return ready()
                    ? new BatchTargetResult(target, TargetExecutionStatus.PENDING, null, "Dispatch pending.", 1)
                    : finalFailure();
        }

        BatchTargetResult finalFailure() {
            return new BatchTargetResult(target, TargetExecutionStatus.FAILED, errorCode, message, 1);
        }
    }
}
