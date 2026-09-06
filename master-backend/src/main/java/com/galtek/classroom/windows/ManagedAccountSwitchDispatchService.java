package com.galtek.classroom.windows;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
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
import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
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
import com.galtek.classroom.windows.ManagedAccountSwitchDtos.ManagedAccountSwitchBatchResponse;
import java.time.Clock;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
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
public class ManagedAccountSwitchDispatchService {

    private static final Set<String> REQUEST_FIELDS = Set.of("targetAccountId", "targetDeviceIds");
    private static final String REQUESTED_BY = "LOCAL_MASTER";
    private static final int MAX_TARGET_DEVICES = 100;
    private static final int MAX_CONCURRENT_TARGETS = 4;

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterAdminRepository adminRepository;
    private final DeviceRepository deviceRepository;
    private final DeviceNetworkBindingRepository bindingRepository;
    private final MasterPairingService pairingService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway remoteOperationGateway;
    private final BatchOperationService batchOperationService;
    private final ManagedAccountSwitchPlanner switchPlanner;
    private final ObjectMapper objectMapper;
    private final Clock clock;

    public ManagedAccountSwitchDispatchService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterAdminRepository adminRepository,
            DeviceRepository deviceRepository,
            DeviceNetworkBindingRepository bindingRepository,
            MasterPairingService pairingService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway remoteOperationGateway,
            BatchOperationService batchOperationService,
            ObjectMapper objectMapper,
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
        this.switchPlanner = new ManagedAccountSwitchPlanner();
        this.objectMapper = objectMapper;
        this.clock = clock;
    }

    public ManagedAccountSwitchBatchResponse dispatch(String classroomId, Map<String, Object> request) {
        requireAuthorizedAndStorage();
        ManagedAccountSwitchDispatchRequest dispatchRequest = requestFrom(request);
        ClassroomResponse classroom = classroomOr404(classroomId);
        OffsetDateTime createdAtUtc = nowUtc();
        String operationId = UUID.randomUUID().toString();

        List<TargetPlan> targetPlans = targetPlans(classroom.classroomId(), dispatchRequest.targetDeviceIds());
        batchOperationService.create(
                classroom.classroomId(),
                BatchOperation.fromTargets(
                        operationId,
                        OperationType.SWITCH_MANAGED_ACCOUNT,
                        REQUESTED_BY,
                        createdAtUtc,
                        targetPlans.stream()
                                .map(TargetPlan::intentResult)
                                .toList()),
                payloadFor(dispatchRequest.targetAccountType()));

        List<BatchTargetResult> finalResults = executeTargets(targetPlans, dispatchRequest.targetAccountType());
        BatchOperation completed = BatchOperation.fromTargets(
                operationId,
                OperationType.SWITCH_MANAGED_ACCOUNT,
                REQUESTED_BY,
                createdAtUtc,
                finalResults);
        batchOperationService.replaceResults(completed);
        return ManagedAccountSwitchBatchResponse.from(completed, dispatchRequest.targetAccountType());
    }

    private List<BatchTargetResult> executeTargets(
            List<TargetPlan> targetPlans,
            ManagedWindowsAccountType targetAccountType) {
        Map<String, BatchTargetResult> finalResultsByDeviceId = new java.util.concurrent.ConcurrentHashMap<>();
        List<TargetPlan> readyTargets = targetPlans.stream()
                .filter(TargetPlan::ready)
                .toList();
        for (TargetPlan targetPlan : targetPlans) {
            if (!targetPlan.ready()) {
                finalResultsByDeviceId.put(targetPlan.deviceId(), targetPlan.finalFailure());
            }
        }

        if (!readyTargets.isEmpty()) {
            ExecutorService executor = Executors.newFixedThreadPool(
                    Math.min(MAX_CONCURRENT_TARGETS, readyTargets.size()),
                    runnable -> {
                        Thread thread = new Thread(runnable, "managed-account-switch-target");
                        thread.setDaemon(true);
                        return thread;
                    });
            try {
                List<Future<BatchTargetResult>> futures = readyTargets.stream()
                        .map(targetPlan -> executor.submit(() -> finalResultsByDeviceId.put(
                                targetPlan.deviceId(),
                                executeReadyTarget(targetPlan, targetAccountType))))
                        .toList();
                for (Future<BatchTargetResult> future : futures) {
                    awaitFuture(future);
                }
            } finally {
                executor.shutdownNow();
            }
        }

        return targetPlans.stream()
                .map(targetPlan -> finalResultsByDeviceId.get(targetPlan.deviceId()))
                .toList();
    }

    private BatchTargetResult executeReadyTarget(
            TargetPlan targetPlan,
            ManagedWindowsAccountType targetAccountType) {
        String snapshotOperationId = UUID.randomUUID().toString();
        RemoteOperationOutcome snapshotOutcome = dispatchAndAwaitSnapshot(targetPlan, snapshotOperationId);
        if (snapshotOutcome.status() != TargetExecutionStatus.SUCCESS
                || snapshotOutcome.windowsSessionState() == null) {
            return outcomeResult(targetPlan.target(), snapshotOutcome);
        }

        WindowsSessionState sessionState = mapSessionState(snapshotOutcome.windowsSessionState());
        ManagedAccountSwitchPreflightItem plan = switchPlanner.planSwitch(
                        UUID.randomUUID().toString(),
                        targetAccountType,
                        List.of(new ManagedAccountSwitchTarget(
                                targetPlan.device(),
                                sessionState,
                                List.of())))
                .targets()
                .getFirst();

        if (plan.action() == ManagedAccountSwitchAction.NO_CHANGE) {
            return new BatchTargetResult(
                    targetPlan.target(),
                    TargetExecutionStatus.NO_CHANGE,
                    null,
                    "Target managed account is already active.",
                    1);
        }

        if (plan.status() == com.galtek.classroom.operations.PreflightStatus.BLOCKED) {
            return failed(targetPlan.target(), plan.errorCode(), plan.message(), 1);
        }

        String switchOperationId = UUID.randomUUID().toString();
        RemoteOperationOutcome switchOutcome = dispatchAndAwaitSwitch(targetPlan, switchOperationId, targetAccountType);
        return outcomeResult(targetPlan.target(), switchOutcome);
    }

    private RemoteOperationOutcome dispatchAndAwaitSnapshot(TargetPlan targetPlan, String operationId) {
        return remoteOperationGateway.getWindowsSessionState(
                        targetPlan.snapshot(),
                        operationId,
                        targetPlan.deviceId())
                .map(handle -> awaitOutcome(handle, remoteOperationGateway.resultTimeout()))
                .orElseGet(() -> RemoteOperationOutcome.failed(
                        ErrorCode.DEVICE_OFFLINE,
                        "Device is offline."));
    }

    private RemoteOperationOutcome dispatchAndAwaitSwitch(
            TargetPlan targetPlan,
            String operationId,
            ManagedWindowsAccountType targetAccountType) {
        return remoteOperationGateway.switchManagedAccount(
                        targetPlan.snapshot(),
                        operationId,
                        targetPlan.deviceId(),
                        networkAccountId(targetAccountType))
                .map(handle -> awaitOutcome(handle, remoteOperationGateway.switchManagedAccountResultTimeout()))
                .orElseGet(() -> RemoteOperationOutcome.failed(
                        ErrorCode.DEVICE_OFFLINE,
                        "Device is offline."));
    }

    private RemoteOperationOutcome awaitOutcome(DispatchHandle handle, java.time.Duration timeout) {
        try {
            return handle.completion().get(timeout.toNanos(), TimeUnit.NANOSECONDS);
        } catch (TimeoutException exception) {
            return remoteOperationGateway.timeout(handle);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return remoteOperationGateway.timeout(handle);
        } catch (ExecutionException exception) {
            return remoteOperationGateway.timeout(handle);
        }
    }

    private void awaitFuture(Future<BatchTargetResult> future) {
        try {
            future.get();
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
        } catch (ExecutionException exception) {
            throw new IllegalStateException("Managed account switch target execution failed.", exception);
        }
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
        Map<String, Device> devices = devicesById(deviceRepository.findByClassroomId(classroomId));
        Map<String, RegisteredNetworkDevice> bindings = bindingsByDeviceId(
                bindingRepository.findCurrentByDeviceIds(targetDeviceIds));
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
        OperationTarget target = new OperationTarget(
                OperationTargetType.DEVICE,
                deviceId,
                device == null ? deviceId : device.displayName());
        if (device == null) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.DEVICE_NOT_FOUND,
                    "Device does not belong to classroom.");
        }

        RegisteredNetworkDevice binding = context.bindings().get(deviceId);
        if (binding == null) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.DEVICE_NOT_REGISTERED,
                    "Device is not registered for network operations.");
        }

        TargetPlan trustBlocked = trustBlock(deviceId, target, binding, context.knownClients());
        if (trustBlocked != null) {
            return trustBlocked;
        }

        ClientConnectionSnapshot snapshot = context.snapshots().get(deviceId);
        if (!authenticatedOnline(snapshot, binding)) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.DEVICE_OFFLINE,
                    "Device is offline.");
        }

        if (!snapshot.capabilities().contains(DeviceCapability.WINDOWS_SESSION_STATE_V1)) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    "Device does not announce WINDOWS_SESSION_STATE_V1.");
        }

        if (!snapshot.capabilities().contains(DeviceCapability.WINDOWS_SESSION_SWITCH_V1)) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    "Device does not announce WINDOWS_SESSION_SWITCH_V1.");
        }

        return TargetPlan.ready(deviceId, device, target, snapshot);
    }

    private TargetPlan trustBlock(
            String deviceId,
            OperationTarget target,
            RegisteredNetworkDevice binding,
            Map<UUID, KnownMasterClient> knownClients) {
        KnownMasterClient client = knownClients.get(binding.networkIdentityId());
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

    private ManagedAccountSwitchDispatchRequest requestFrom(Map<String, Object> request) {
        requireKnownBody(request);
        Object rawTargetAccountId = request.get("targetAccountId");
        if (!(rawTargetAccountId instanceof String targetAccountId) || targetAccountId.isBlank()) {
            throw validation("targetAccountId is required.");
        }

        ManagedWindowsAccountType targetAccountType;
        try {
            targetAccountType = ManagedWindowsAccountType.valueOf(targetAccountId.trim());
        } catch (IllegalArgumentException exception) {
            throw validation("targetAccountId must be PRIMARY or SECONDARY.");
        }

        return new ManagedAccountSwitchDispatchRequest(targetAccountType, targetDeviceIdsFrom(request));
    }

    private List<String> targetDeviceIdsFrom(Map<String, Object> request) {
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

    private OperationPayload payloadFor(ManagedWindowsAccountType targetAccountType) {
        try {
            return new OperationPayload(1, objectMapper.writeValueAsString(Map.of(
                    "schemaVersion", 1,
                    "targetAccountId", targetAccountType.name())));
        } catch (JsonProcessingException exception) {
            throw new IllegalStateException("Managed account switch payload could not be serialized.", exception);
        }
    }

    private BatchTargetResult outcomeResult(OperationTarget target, RemoteOperationOutcome outcome) {
        return new BatchTargetResult(
                target,
                outcome.status(),
                outcome.errorCode(),
                outcome.message(),
                1);
    }

    private BatchTargetResult failed(
            OperationTarget target,
            ErrorCode errorCode,
            String message,
            int attempt) {
        return new BatchTargetResult(target, TargetExecutionStatus.FAILED, errorCode, message, attempt);
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

    private ManagedWindowsAccountId networkAccountId(ManagedWindowsAccountType targetAccountType) {
        return switch (targetAccountType) {
            case PRIMARY -> ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY;
            case SECONDARY -> ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY;
        };
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

    private record ManagedAccountSwitchDispatchRequest(
            ManagedWindowsAccountType targetAccountType,
            List<String> targetDeviceIds) {
    }

    private record TargetContext(
            Map<String, Device> devices,
            Map<String, RegisteredNetworkDevice> bindings,
            Map<String, ClientConnectionSnapshot> snapshots,
            Map<UUID, KnownMasterClient> knownClients) {
    }

    private record TargetPlan(
            String deviceId,
            Device device,
            OperationTarget target,
            ClientConnectionSnapshot snapshot,
            ErrorCode errorCode,
            String message) {

        static TargetPlan ready(
                String deviceId,
                Device device,
                OperationTarget target,
                ClientConnectionSnapshot snapshot) {
            return new TargetPlan(deviceId, device, target, snapshot, null, null);
        }

        static TargetPlan blocked(
                String deviceId,
                OperationTarget target,
                ErrorCode errorCode,
                String message) {
            return new TargetPlan(deviceId, null, target, null, errorCode, message);
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
