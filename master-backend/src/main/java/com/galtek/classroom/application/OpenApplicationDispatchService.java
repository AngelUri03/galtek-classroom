package com.galtek.classroom.application;

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
import com.galtek.classroom.network.v1.OpenApplicationOperationParameters;
import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchOperationService;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationDtos.OperationBatchResponse;
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
public class OpenApplicationDispatchService {

    private static final Set<String> REQUEST_FIELDS = Set.of("applicationId", "targetDeviceIds");
    private static final String REQUESTED_BY = "LOCAL_MASTER";

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterAdminRepository adminRepository;
    private final ApplicationDefinitionRepository applicationDefinitionRepository;
    private final DeviceRepository deviceRepository;
    private final DeviceNetworkBindingRepository bindingRepository;
    private final MasterPairingService pairingService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway remoteOperationGateway;
    private final BatchOperationService batchOperationService;
    private final ObjectMapper objectMapper;
    private final Clock clock;

    public OpenApplicationDispatchService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterAdminRepository adminRepository,
            ApplicationDefinitionRepository applicationDefinitionRepository,
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
        this.applicationDefinitionRepository = applicationDefinitionRepository;
        this.deviceRepository = deviceRepository;
        this.bindingRepository = bindingRepository;
        this.pairingService = pairingService;
        this.connectionRegistry = connectionRegistry;
        this.remoteOperationGateway = remoteOperationGateway;
        this.batchOperationService = batchOperationService;
        this.objectMapper = objectMapper;
        this.clock = clock;
    }

    public OperationBatchResponse dispatch(String classroomId, Map<String, Object> request) {
        requireAuthorizedAndStorage();
        OpenApplicationDispatchRequest dispatchRequest = requestFrom(request);
        ClassroomResponse classroom = classroomOr404(classroomId);
        ApplicationDefinition application = applicationForClassroomOrReject(
                dispatchRequest.applicationId(),
                classroom);
        OffsetDateTime createdAtUtc = nowUtc();
        String operationId = UUID.randomUUID().toString();

        OpenApplicationOperationParameters parameters = OpenApplicationOperationParameters.newBuilder()
                .setApplicationId(application.applicationId())
                .build();
        List<TargetPlan> targetPlans = targetPlans(
                classroom.classroomId(),
                dispatchRequest.targetDeviceIds(),
                parameters);
        List<BatchTargetResult> intentTargets = targetPlans.stream()
                .map(TargetPlan::intentResult)
                .toList();
        batchOperationService.create(
                classroom.classroomId(),
                BatchOperation.fromTargets(
                        operationId,
                        OperationType.OPEN_APPLICATION,
                        REQUESTED_BY,
                        createdAtUtc,
                        intentTargets),
                payloadFor(application.applicationId()));

        Map<String, DispatchHandle> dispatched = new LinkedHashMap<>();
        Map<String, BatchTargetResult> finalResultsByDeviceId = new LinkedHashMap<>();
        for (TargetPlan targetPlan : targetPlans) {
            if (!targetPlan.ready()) {
                finalResultsByDeviceId.put(targetPlan.deviceId(), targetPlan.finalFailure());
                continue;
            }

            var handle = remoteOperationGateway.dispatch(
                    targetPlan.snapshot(),
                    OperationType.OPEN_APPLICATION,
                    operationId,
                    targetPlan.deviceId(),
                    targetPlan.parameters());
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
                OperationType.OPEN_APPLICATION,
                REQUESTED_BY,
                createdAtUtc,
                finalResults);
        batchOperationService.replaceResults(completed);
        return OperationBatchResponse.from(completed);
    }

    private ApplicationDefinition applicationForClassroomOrReject(
            String applicationId,
            ClassroomResponse classroom) {
        ApplicationDefinition application = applicationDefinitionRepository.findActiveById(applicationId)
                .orElseThrow(() -> applicationUnavailable());
        if (!classroom.authorizedApplicationIds().contains(application.applicationId())) {
            throw applicationUnavailable();
        }
        return application;
    }

    private List<TargetPlan> targetPlans(
            String classroomId,
            List<String> targetDeviceIds,
            OpenApplicationOperationParameters parameters) {
        TargetContext context = targetContext(classroomId, targetDeviceIds);
        List<TargetPlan> plans = new ArrayList<>(targetDeviceIds.size());
        for (String deviceId : targetDeviceIds) {
            TargetPlan preflight = preflight(
                    deviceId,
                    context,
                    DeviceCapability.OPEN_APPLICATION_V1,
                    "Device does not announce OPEN_APPLICATION_V1.");
            plans.add(preflight.ready() ? preflight.withParameters(parameters) : preflight);
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

    private TargetPlan preflight(
            String deviceId,
            TargetContext context,
            DeviceCapability requiredCapability,
            String missingCapabilityMessage) {
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

        if (!snapshot.capabilities().contains(requiredCapability)) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    missingCapabilityMessage);
        }

        return TargetPlan.ready(deviceId, target, snapshot);
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

    private OperationPayload payloadFor(String applicationId) {
        try {
            return new OperationPayload(1, objectMapper.writeValueAsString(Map.of(
                    "schemaVersion", 1,
                    "applicationId", applicationId)));
        } catch (JsonProcessingException exception) {
            throw new IllegalStateException("Open application payload could not be serialized.", exception);
        }
    }

    private OpenApplicationDispatchRequest requestFrom(Map<String, Object> request) {
        requireKnownBody(request);
        Object rawApplicationId = request.get("applicationId");
        if (!(rawApplicationId instanceof String applicationId) || applicationId.isBlank()) {
            throw validation("applicationId is required.");
        }
        return new OpenApplicationDispatchRequest(applicationId.trim(), targetDeviceIdsFrom(request));
    }

    private List<String> targetDeviceIdsFrom(Map<String, Object> request) {
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

    private ApiException applicationUnavailable() {
        return new ApiException(
                HttpStatus.FORBIDDEN,
                ErrorCode.APPLICATION_NOT_ALLOWED,
                "Application is not available for this classroom.");
    }

    private ApiException notFound(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.NOT_FOUND, errorCode, message);
    }

    private ApiException validation(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, message);
    }

    private record OpenApplicationDispatchRequest(
            String applicationId,
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
            OperationTarget target,
            ClientConnectionSnapshot snapshot,
            OpenApplicationOperationParameters parameters,
            ErrorCode errorCode,
            String message) {

        static TargetPlan ready(
                String deviceId,
                OperationTarget target,
                ClientConnectionSnapshot snapshot) {
            return new TargetPlan(deviceId, target, snapshot, null, null, null);
        }

        static TargetPlan blocked(
                String deviceId,
                OperationTarget target,
                ErrorCode errorCode,
                String message) {
            return new TargetPlan(deviceId, target, null, null, errorCode, message);
        }

        TargetPlan withParameters(OpenApplicationOperationParameters parameters) {
            return new TargetPlan(deviceId, target, snapshot, parameters, null, null);
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
