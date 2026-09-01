package com.galtek.classroom.network;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;

import com.galtek.classroom.admin.AdminDtos.OperationResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationKey;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.MasterRemoteOperationGateway.StatusQueryHandle;
import com.galtek.classroom.network.v1.NetworkOperationErrorCode;
import com.galtek.classroom.network.v1.NetworkOperationType;
import com.galtek.classroom.network.v1.OperationExecutionStatus;
import com.galtek.classroom.network.v1.OperationResult;
import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchOperationService;
import com.galtek.classroom.operations.BatchOperationStatus;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationTarget;
import com.galtek.classroom.operations.OperationTargetType;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageState;
import java.time.Duration;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;

class PowerOperationReconciliationServiceTest {

    private static final OffsetDateTime CREATED_AT = OffsetDateTime.ofInstant(
            Instant.parse("2026-08-31T12:00:00Z"),
            ZoneOffset.UTC);
    private static final Instant RECOVERY_CUTOFF = Instant.parse("2026-08-31T12:30:00Z");
    private static final OffsetDateTime RECOVERY_CUTOFF_AT = OffsetDateTime.ofInstant(
            RECOVERY_CUTOFF,
            ZoneOffset.UTC);
    private static final UUID NETWORK_ID = UUID.fromString("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static final UUID INSTALLATION_ID = UUID.fromString("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

    private final MasterAccessGuard accessGuard = org.mockito.Mockito.mock(MasterAccessGuard.class);
    private final MasterAdminRepository adminRepository = org.mockito.Mockito.mock(MasterAdminRepository.class);
    private final DeviceNetworkBindingRepository bindingRepository =
            org.mockito.Mockito.mock(DeviceNetworkBindingRepository.class);
    private final MasterPairingService pairingService = org.mockito.Mockito.mock(MasterPairingService.class);
    private final ClientConnectionRegistry connectionRegistry = org.mockito.Mockito.mock(ClientConnectionRegistry.class);
    private final MasterRemoteOperationGateway gateway = org.mockito.Mockito.mock(MasterRemoteOperationGateway.class);
    private final BatchOperationService batchOperationService = org.mockito.Mockito.mock(BatchOperationService.class);
    private final MasterStorageState storageState = new MasterStorageState();

    private PowerOperationReconciliationService service;

    @BeforeEach
    void setUp() {
        storageState.markReady();
        when(accessGuard.requireAuthorized()).thenReturn(new MasterAuthorizationResponse(
                "AUTHORIZED",
                true,
                true,
                "AULA\\Maestra",
                "AULA\\Maestra"));
        when(bindingRepository.findCurrentByDeviceId("PC01")).thenReturn(Optional.of(binding("PC01")));
        when(pairingService.knownClient(NETWORK_ID)).thenReturn(Optional.of(pairedClient()));
        when(connectionRegistry.findByDeviceId("PC01")).thenReturn(Optional.of(snapshot("PC01")));
        when(gateway.statusQueryTimeout()).thenReturn(Duration.ofMillis(1));
        when(adminRepository.findOperation(anyString())).thenReturn(Optional.of(operationResponse("batch-1")));

        service = new PowerOperationReconciliationService(
                accessGuard,
                storageState,
                adminRepository,
                bindingRepository,
                pairingService,
                connectionRegistry,
                gateway,
                batchOperationService);
    }

    @Test
    void lateOperationResultSuccessChangesUnknownTargetToSuccess() {
        BatchOperation operation = operation(
                "batch-1",
                OperationType.SHUTDOWN,
                unknownTarget("PC01"));
        when(batchOperationService.findById("batch-1"))
                .thenReturn(Optional.of(operation), Optional.of(operation));

        service.handleLateOperationResult(
                result("batch-1", "PC01", NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN,
                        OperationExecutionStatus.OPERATION_EXECUTION_STATUS_SUCCESS,
                        NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_UNSPECIFIED),
                NETWORK_ID,
                "connection-1");

        BatchOperation updated = capturedReplacement();
        assertThat(updated.targets()).singleElement()
                .satisfies(target -> assertThat(target.status()).isEqualTo(TargetExecutionStatus.SUCCESS));
    }

    @Test
    void lateOperationResultFailedChangesUnknownErrorToRealError() {
        BatchOperation operation = operation(
                "batch-1",
                OperationType.RESTART,
                unknownTarget("PC01"));
        when(batchOperationService.findById("batch-1"))
                .thenReturn(Optional.of(operation), Optional.of(operation));

        service.handleLateOperationResult(
                result("batch-1", "PC01", NetworkOperationType.NETWORK_OPERATION_TYPE_RESTART,
                        OperationExecutionStatus.OPERATION_EXECUTION_STATUS_FAILED,
                        NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_FAILED),
                NETWORK_ID,
                "connection-1");

        BatchOperation updated = capturedReplacement();
        assertThat(updated.targets()).singleElement()
                .satisfies(target -> {
                    assertThat(target.status()).isEqualTo(TargetExecutionStatus.FAILED);
                    assertThat(target.errorCode()).isEqualTo(ErrorCode.POWER_CONTROL_FAILED);
                });
    }

    @Test
    void lateResultFromOtherDeviceDoesNotModifyTarget() {
        when(connectionRegistry.findByDeviceId("PC02")).thenReturn(Optional.empty());
        when(batchOperationService.findById("batch-1")).thenReturn(Optional.of(operation(
                "batch-1",
                OperationType.SHUTDOWN,
                unknownTarget("PC01"))));

        service.handleLateOperationResult(
                result("batch-1", "PC02", NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN,
                        OperationExecutionStatus.OPERATION_EXECUTION_STATUS_SUCCESS,
                        NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_UNSPECIFIED),
                UUID.randomUUID(),
                "connection-2");

        verify(batchOperationService, never()).replaceResults(any());
    }

    @Test
    void lateFailedResultDoesNotDegradeSuccessfulTarget() {
        BatchOperation operation = operation(
                "batch-1",
                OperationType.SHUTDOWN,
                successTarget("PC01"));
        when(batchOperationService.findById("batch-1"))
                .thenReturn(Optional.of(operation), Optional.of(operation));

        service.handleLateOperationResult(
                result("batch-1", "PC01", NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN,
                        OperationExecutionStatus.OPERATION_EXECUTION_STATUS_FAILED,
                        NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_FAILED),
                NETWORK_ID,
                "connection-1");

        verify(batchOperationService, never()).replaceResults(any());
    }

    @Test
    void manualStatusQueryKnownReconcilesUnknownTarget() {
        BatchOperation operation = operation(
                "batch-1",
                OperationType.SHUTDOWN,
                unknownTarget("PC01"));
        when(batchOperationService.findById("batch-1"))
                .thenReturn(Optional.of(operation), Optional.of(operation));
        when(gateway.queryStatus(snapshot("PC01"), OperationType.SHUTDOWN, "batch-1", "PC01"))
                .thenReturn(Optional.of(statusHandle(
                        "batch-1",
                        "PC01",
                        Optional.of(RemoteOperationOutcome.success("Agent reported operation success.")))));

        service.reconcileOperation("batch-1", null);

        verify(accessGuard).requireAuthorized();
        assertThat(capturedReplacement().status()).isEqualTo(BatchOperationStatus.SUCCESS);
        verify(gateway, never()).dispatch(any(), any(), anyString(), anyString());
    }

    @Test
    void statusQueryUnknownOrTimeoutPreservesOperationResultUnknown() {
        BatchOperation operation = operation(
                "batch-1",
                OperationType.SHUTDOWN,
                unknownTarget("PC01"));
        when(batchOperationService.findById("batch-1")).thenReturn(Optional.of(operation));
        when(gateway.queryStatus(snapshot("PC01"), OperationType.SHUTDOWN, "batch-1", "PC01"))
                .thenReturn(Optional.of(statusHandle("batch-1", "PC01", Optional.empty())));

        service.reconcileOperation("batch-1", null);

        verify(batchOperationService, never()).replaceResults(any());

        CompletableFuture<Optional<RemoteOperationOutcome>> neverCompletes = new CompletableFuture<>();
        StatusQueryHandle timeoutHandle = new StatusQueryHandle(
                new RemoteOperationKey("PC01", "batch-2"),
                neverCompletes);
        when(batchOperationService.findById("batch-2")).thenReturn(Optional.of(operation("batch-2", OperationType.SHUTDOWN,
                unknownTarget("PC01"))));
        when(gateway.queryStatus(snapshot("PC01"), OperationType.SHUTDOWN, "batch-2", "PC01"))
                .thenReturn(Optional.of(timeoutHandle));
        when(gateway.timeoutStatusQuery(timeoutHandle)).thenReturn(Optional.empty());

        service.reconcileOperation("batch-2", null);

        verify(gateway).timeoutStatusQuery(timeoutHandle);
    }

    @Test
    void reconnectQueriesOnlyUnknownTargetsForConnectedDeviceAndDoesNotResend() {
        BatchOperation operation = operation(
                "batch-1",
                OperationType.RESTART,
                successTarget("PC00"),
                unknownTarget("PC01"));
        when(batchOperationService.powerOperationsWithUnknownTarget("PC01")).thenReturn(List.of(operation));
        when(batchOperationService.findById("batch-1")).thenReturn(Optional.of(operation));
        when(gateway.queryStatus(snapshot("PC01"), OperationType.RESTART, "batch-1", "PC01"))
                .thenReturn(Optional.of(statusHandle("batch-1", "PC01", Optional.empty())));

        service.reconcileUnknownTargetsForReconnectedDevice(snapshot("PC01"));

        verify(batchOperationService).powerOperationsWithUnknownTarget("PC01");
        verify(gateway).queryStatus(snapshot("PC01"), OperationType.RESTART, "batch-1", "PC01");
        verify(gateway, never()).dispatch(any(), any(), anyString(), anyString());
    }

    @Test
    void startupRecoveryTransformsPendingPowerTargetsCreatedBeforeCutoffToUnknownWithoutClients() {
        BatchOperation operation = operation(
                "batch-1",
                OperationType.RESTART,
                CREATED_AT.minusMinutes(5),
                pendingTarget("PC01"));
        when(batchOperationService.powerOperationsWithPendingTargetsCreatedBefore(RECOVERY_CUTOFF_AT))
                .thenReturn(List.of(operation));

        int recovered = service.recoverOrphanedPendingPowerTargets(RECOVERY_CUTOFF);

        assertThat(recovered).isEqualTo(1);
        BatchOperation updated = capturedReplacement();
        assertThat(updated.targets()).singleElement()
                .satisfies(target -> {
                    assertThat(target.status()).isEqualTo(TargetExecutionStatus.FAILED);
                    assertThat(target.errorCode()).isEqualTo(ErrorCode.OPERATION_RESULT_UNKNOWN);
                });
        verify(batchOperationService).powerOperationsWithPendingTargetsCreatedBefore(RECOVERY_CUTOFF_AT);
        verifyNoInteractions(connectionRegistry);
        verify(gateway, never()).dispatch(any(), any(), anyString(), anyString());
    }

    @Test
    void startupRecoveryLeavesPendingPowerTargetsCreatedAfterCutoffUnchanged() {
        BatchOperation operation = operation(
                "batch-new",
                OperationType.SHUTDOWN,
                RECOVERY_CUTOFF_AT.plusSeconds(1),
                pendingTarget("PC01"));
        when(batchOperationService.powerOperationsWithPendingTargetsCreatedBefore(RECOVERY_CUTOFF_AT))
                .thenReturn(List.of(operation));

        int recovered = service.recoverOrphanedPendingPowerTargets(RECOVERY_CUTOFF);

        assertThat(recovered).isZero();
        verify(batchOperationService, never()).replaceResults(any());
        verify(gateway, never()).dispatch(any(), any(), anyString(), anyString());
    }

    @Test
    void startupRecoveryLeavesNonPowerOperationsUnchanged() {
        BatchOperation operation = operation(
                "batch-open-url",
                OperationType.OPEN_URL,
                CREATED_AT.minusMinutes(5),
                pendingTarget("PC01"));
        when(batchOperationService.powerOperationsWithPendingTargetsCreatedBefore(RECOVERY_CUTOFF_AT))
                .thenReturn(List.of(operation));

        int recovered = service.recoverOrphanedPendingPowerTargets(RECOVERY_CUTOFF);

        assertThat(recovered).isZero();
        verify(batchOperationService, never()).replaceResults(any());
        verify(gateway, never()).dispatch(any(), any(), anyString(), anyString());
    }

    @Test
    void reconcileRejectsNonPowerOperationAndArbitraryBody() {
        when(batchOperationService.findById("batch-1")).thenReturn(Optional.of(operation(
                "batch-1",
                OperationType.OPEN_URL,
                unknownTarget("PC01"))));

        assertThatThrownBy(() -> service.reconcileOperation("batch-1", null))
                .isInstanceOf(ApiException.class)
                .hasMessageContaining("Only SHUTDOWN and RESTART");

        assertThatThrownBy(() -> service.reconcileOperation("batch-1", Map.of("retry", true)))
                .isInstanceOf(ApiException.class)
                .hasMessageContaining("body is not supported");
    }

    @Test
    void offlineTargetRemainsUnknownDuringManualReconcile() {
        when(connectionRegistry.findByDeviceId("PC01")).thenReturn(Optional.empty());
        when(batchOperationService.findById("batch-1")).thenReturn(Optional.of(operation(
                "batch-1",
                OperationType.SHUTDOWN,
                unknownTarget("PC01"))));

        service.reconcileOperation("batch-1", null);

        verify(gateway, never()).queryStatus(any(), any(), anyString(), anyString());
        verify(batchOperationService, never()).replaceResults(any());
    }

    private BatchOperation capturedReplacement() {
        ArgumentCaptor<BatchOperation> captor = ArgumentCaptor.forClass(BatchOperation.class);
        verify(batchOperationService).replaceResults(captor.capture());
        return captor.getValue();
    }

    private BatchOperation operation(
            String operationId,
            OperationType operationType,
            BatchTargetResult... targets) {
        return operation(operationId, operationType, CREATED_AT, targets);
    }

    private BatchOperation operation(
            String operationId,
            OperationType operationType,
            OffsetDateTime createdAtUtc,
            BatchTargetResult... targets) {
        return BatchOperation.fromTargets(
                operationId,
                operationType,
                "LOCAL_MASTER",
                createdAtUtc,
                List.of(targets));
    }

    private BatchTargetResult unknownTarget(String deviceId) {
        return new BatchTargetResult(
                new OperationTarget(OperationTargetType.DEVICE, deviceId, deviceId),
                TargetExecutionStatus.FAILED,
                ErrorCode.OPERATION_RESULT_UNKNOWN,
                "Operation result is unknown.",
                1);
    }

    private BatchTargetResult successTarget(String deviceId) {
        return new BatchTargetResult(
                new OperationTarget(OperationTargetType.DEVICE, deviceId, deviceId),
                TargetExecutionStatus.SUCCESS,
                null,
                "Agent reported operation success.",
                1);
    }

    private BatchTargetResult pendingTarget(String deviceId) {
        return new BatchTargetResult(
                new OperationTarget(OperationTargetType.DEVICE, deviceId, deviceId),
                TargetExecutionStatus.PENDING,
                null,
                "Dispatch pending.",
                1);
    }

    private OperationResult result(
            String operationId,
            String deviceId,
            NetworkOperationType operationType,
            OperationExecutionStatus status,
            NetworkOperationErrorCode errorCode) {
        return OperationResult.newBuilder()
                .setOperationId(operationId)
                .setTargetDeviceId(deviceId)
                .setOperationType(operationType)
                .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                .setStatus(status)
                .setErrorCode(errorCode)
                .build();
    }

    private StatusQueryHandle statusHandle(
            String operationId,
            String deviceId,
            Optional<RemoteOperationOutcome> outcome) {
        return new StatusQueryHandle(
                new RemoteOperationKey(deviceId, operationId),
                CompletableFuture.completedFuture(outcome));
    }

    private ClientConnectionSnapshot snapshot(String deviceId) {
        return new ClientConnectionSnapshot(
                NETWORK_ID,
                INSTALLATION_ID,
                deviceId,
                "classroom-1",
                true,
                deviceId,
                deviceId.toLowerCase(),
                DeviceStatus.ONLINE,
                "0.5.0-test",
                Set.of(DeviceCapability.HEARTBEAT_V1, DeviceCapability.OPERATION_FRAMEWORK_V1,
                        DeviceCapability.POWER_CONTROL_V1),
                Instant.parse("2026-08-31T12:00:00Z"),
                Instant.parse("2026-08-31T12:00:00Z"),
                null,
                "connection-1",
                null);
    }

    private RegisteredNetworkDevice binding(String deviceId) {
        return new RegisteredNetworkDevice(
                "binding-1",
                deviceId,
                "classroom-1",
                INSTALLATION_ID,
                NETWORK_ID,
                "a".repeat(64),
                deviceId,
                deviceId.toLowerCase(),
                "0.5.0-test",
                Set.of(DeviceCapability.POWER_CONTROL_V1),
                CREATED_AT,
                CREATED_AT,
                true,
                0);
    }

    private KnownMasterClient pairedClient() {
        return new KnownMasterClient(
                PairingStatus.PAIRED,
                NETWORK_ID,
                INSTALLATION_ID,
                "a".repeat(64),
                "public-key",
                Instant.parse("2026-08-31T12:00:00Z"),
                null);
    }

    private OperationResponse operationResponse(String operationId) {
        return new OperationResponse(
                operationId,
                "classroom-1",
                "SHUTDOWN",
                "LOCAL_MASTER",
                CREATED_AT,
                "SUCCESS",
                1,
                List.of(),
                1);
    }
}
