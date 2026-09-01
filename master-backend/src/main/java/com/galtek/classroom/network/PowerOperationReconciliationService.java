package com.galtek.classroom.network;

import com.galtek.classroom.admin.AdminDtos.OperationResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.MasterRemoteOperationGateway.StatusQueryHandle;
import com.galtek.classroom.network.v1.NetworkOperationType;
import com.galtek.classroom.network.v1.OperationResult;
import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchOperationService;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Optional;
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
public class PowerOperationReconciliationService {

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterAdminRepository adminRepository;
    private final DeviceNetworkBindingRepository bindingRepository;
    private final MasterPairingService pairingService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway remoteOperationGateway;
    private final BatchOperationService batchOperationService;

    public PowerOperationReconciliationService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterAdminRepository adminRepository,
            DeviceNetworkBindingRepository bindingRepository,
            MasterPairingService pairingService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway remoteOperationGateway,
            BatchOperationService batchOperationService) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.adminRepository = adminRepository;
        this.bindingRepository = bindingRepository;
        this.pairingService = pairingService;
        this.connectionRegistry = connectionRegistry;
        this.remoteOperationGateway = remoteOperationGateway;
        this.batchOperationService = batchOperationService;
    }

    public OperationResponse reconcileOperation(String operationId, Map<String, Object> requestBody) {
        requireAuthorizedAndStorage();
        if (requestBody != null && !requestBody.isEmpty()) {
            throw validation("Reconcile request body is not supported.");
        }

        BatchOperation operation = operationOr404(operationId);
        if (!isPowerOperation(operation.type())) {
            throw validation("Only SHUTDOWN and RESTART operations can be reconciled.");
        }

        reconcileUnknownTargets(operation);
        return adminRepository.findOperation(operation.operationId()).orElseThrow();
    }

    public void reconcileUnknownTargetsForReconnectedDevice(ClientConnectionSnapshot snapshot) {
        if (!storageReady()
                || snapshot == null
                || snapshot.deviceId() == null
                || snapshot.status() != DeviceStatus.ONLINE
                || !snapshot.registered()) {
            return;
        }

        List<BatchOperation> operations =
                batchOperationService.powerOperationsWithUnknownTarget(snapshot.deviceId());
        for (BatchOperation operation : operations) {
            Optional<RemoteOperationOutcome> outcome = queryStatus(
                    snapshot,
                    operation.type(),
                    operation.operationId(),
                    snapshot.deviceId());
            outcome.ifPresent(value -> applyOutcomeIfUnknown(operation.operationId(), snapshot.deviceId(), value));
        }
    }

    public void handleLateOperationResult(
            OperationResult result,
            UUID clientNetworkIdentityId,
            String connectionId) {
        if (!storageReady()
                || result == null
                || result.getOperationId().isBlank()
                || result.getTargetDeviceId().isBlank()) {
            return;
        }

        BatchOperation operation = batchOperationService.findById(result.getOperationId()).orElse(null);
        if (operation == null
                || !isPowerOperation(operation.type())
                || toNetworkOperationType(operation.type()) != result.getOperationType()
                || !authenticatedOnline(result.getTargetDeviceId(), clientNetworkIdentityId, connectionId)) {
            return;
        }

        applyOutcomeIfUnknown(
                operation.operationId(),
                result.getTargetDeviceId(),
                MasterRemoteOperationGateway.outcomeFromResult(result));
    }

    public int recoverOrphanedPendingPowerTargets(Instant recoveryCutoffUtc) {
        if (!storageReady()) {
            return 0;
        }

        OffsetDateTime cutoffUtc = OffsetDateTime.ofInstant(
                Optional.ofNullable(recoveryCutoffUtc).orElse(Instant.EPOCH),
                ZoneOffset.UTC);
        int recovered = 0;
        for (BatchOperation operation : batchOperationService.powerOperationsWithPendingTargetsCreatedBefore(cutoffUtc)) {
            if (!isPowerOperation(operation.type()) || !operation.createdAtUtc().isBefore(cutoffUtc)) {
                continue;
            }

            List<BatchTargetResult> targets = new ArrayList<>(operation.targets().size());
            boolean changed = false;
            for (BatchTargetResult target : operation.targets()) {
                if (target.status() == TargetExecutionStatus.PENDING) {
                    targets.add(new BatchTargetResult(
                            target.target(),
                            TargetExecutionStatus.FAILED,
                            ErrorCode.OPERATION_RESULT_UNKNOWN,
                            "Operation result is unknown after Master restart.",
                            target.attempt()));
                    changed = true;
                    recovered++;
                } else {
                    targets.add(target);
                }
            }

            if (changed) {
                batchOperationService.replaceResults(BatchOperation.fromTargets(
                        operation.operationId(),
                        operation.type(),
                        operation.requestedBy(),
                        operation.createdAtUtc(),
                        targets));
            }
        }

        return recovered;
    }

    private void reconcileUnknownTargets(BatchOperation operation) {
        for (BatchTargetResult target : operation.targets()) {
            if (!isUnknownPowerTarget(target)) {
                continue;
            }

            String deviceId = target.target().targetId();
            ClientConnectionSnapshot snapshot = connectionRegistry.findByDeviceId(deviceId).orElse(null);
            if (!authenticatedOnline(snapshot, deviceId)) {
                continue;
            }

            Optional<RemoteOperationOutcome> outcome = queryStatus(snapshot, operation.type(), operation.operationId(), deviceId);
            outcome.ifPresent(value -> applyOutcomeIfUnknown(operation.operationId(), deviceId, value));
        }
    }

    private Optional<RemoteOperationOutcome> queryStatus(
            ClientConnectionSnapshot snapshot,
            OperationType operationType,
            String operationId,
            String deviceId) {
        Optional<StatusQueryHandle> handle = remoteOperationGateway.queryStatus(
                snapshot,
                operationType,
                operationId,
                deviceId);
        if (handle.isEmpty()) {
            return Optional.empty();
        }

        try {
            return handle.get().completion().get(
                    remoteOperationGateway.statusQueryTimeout().toNanos(),
                    TimeUnit.NANOSECONDS);
        } catch (TimeoutException exception) {
            return remoteOperationGateway.timeoutStatusQuery(handle.get());
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return remoteOperationGateway.timeoutStatusQuery(handle.get());
        } catch (ExecutionException exception) {
            return remoteOperationGateway.timeoutStatusQuery(handle.get());
        }
    }

    private synchronized void applyOutcomeIfUnknown(
            String operationId,
            String deviceId,
            RemoteOperationOutcome outcome) {
        BatchOperation current = batchOperationService.findById(operationId).orElse(null);
        if (current == null || !isPowerOperation(current.type())) {
            return;
        }

        List<BatchTargetResult> targets = new ArrayList<>(current.targets().size());
        boolean changed = false;
        for (BatchTargetResult target : current.targets()) {
            if (target.target().targetId().equals(deviceId) && isUnknownPowerTarget(target)) {
                targets.add(new BatchTargetResult(
                        target.target(),
                        outcome.status(),
                        outcome.errorCode(),
                        outcome.message(),
                        target.attempt()));
                changed = true;
            } else {
                targets.add(target);
            }
        }

        if (changed) {
            batchOperationService.replaceResults(BatchOperation.fromTargets(
                    current.operationId(),
                    current.type(),
                    current.requestedBy(),
                    current.createdAtUtc(),
                    targets));
        }
    }

    private boolean authenticatedOnline(String deviceId, UUID clientNetworkIdentityId, String connectionId) {
        ClientConnectionSnapshot snapshot = connectionRegistry.findByDeviceId(deviceId).orElse(null);
        return snapshot != null
                && snapshot.clientNetworkIdentityId().equals(clientNetworkIdentityId)
                && connectionId != null
                && connectionId.equals(snapshot.connectionId())
                && authenticatedOnline(snapshot, deviceId);
    }

    private boolean authenticatedOnline(ClientConnectionSnapshot snapshot, String deviceId) {
        if (snapshot == null
                || snapshot.status() != DeviceStatus.ONLINE
                || !snapshot.registered()
                || snapshot.connectionId() == null
                || snapshot.deviceId() == null
                || !snapshot.deviceId().equals(deviceId)) {
            return false;
        }

        RegisteredNetworkDevice binding = bindingRepository.findCurrentByDeviceId(deviceId).orElse(null);
        if (binding == null
                || !snapshot.clientNetworkIdentityId().equals(binding.networkIdentityId())
                || !snapshot.clientInstallationId().equals(binding.installationId())
                || !snapshot.deviceId().equals(binding.deviceId())) {
            return false;
        }

        KnownMasterClient client = pairingService.knownClient(binding.networkIdentityId()).orElse(null);
        return client != null
                && client.status() == PairingStatus.PAIRED
                && client.clientInstallationId().equals(binding.installationId())
                && client.clientPublicKeyFingerprint().equals(binding.publicKeyFingerprint());
    }

    private boolean isUnknownPowerTarget(BatchTargetResult target) {
        return target.status() == TargetExecutionStatus.FAILED
                && target.errorCode() == ErrorCode.OPERATION_RESULT_UNKNOWN;
    }

    private BatchOperation operationOr404(String operationId) {
        String cleanOperationId = required(operationId, "operationId");
        return batchOperationService.findById(cleanOperationId)
                .orElseThrow(() -> new ApiException(
                        HttpStatus.NOT_FOUND,
                        ErrorCode.OPERATION_NOT_FOUND,
                        "Operation was not found."));
    }

    private boolean isPowerOperation(OperationType operationType) {
        return operationType == OperationType.SHUTDOWN || operationType == OperationType.RESTART;
    }

    private NetworkOperationType toNetworkOperationType(OperationType operationType) {
        return switch (operationType) {
            case SHUTDOWN -> NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN;
            case RESTART -> NetworkOperationType.NETWORK_OPERATION_TYPE_RESTART;
            default -> NetworkOperationType.NETWORK_OPERATION_TYPE_UNSPECIFIED;
        };
    }

    private String required(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw validation(fieldName + " is required.");
        }
        return value.trim();
    }

    private MasterAuthorizationResponse requireAuthorizedAndStorage() {
        MasterAuthorizationResponse authorization = masterAccessGuard.requireAuthorized();
        if (!storageReady()) {
            MasterStorageHealth health = storageState.health();
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

    private boolean storageReady() {
        return storageState.health().status() == MasterStorageStatus.READY;
    }

    private ApiException validation(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, message);
    }
}
