package com.galtek.classroom.network;

import com.galtek.classroom.network.v1.MasterEnvelope;
import com.galtek.classroom.network.v1.NetworkOperationErrorCode;
import com.galtek.classroom.network.v1.NetworkOperationType;
import com.galtek.classroom.network.v1.OperationAcceptanceStatus;
import com.galtek.classroom.network.v1.OperationAccepted;
import com.galtek.classroom.network.v1.OperationExecutionStatus;
import com.galtek.classroom.network.v1.OperationRequest;
import com.galtek.classroom.network.v1.OperationResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import io.grpc.stub.StreamObserver;
import java.time.Clock;
import java.time.Duration;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentMap;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class MasterRemoteOperationGateway {

    private static final Duration DEFAULT_RESULT_TIMEOUT = Duration.ofSeconds(5);

    private final Clock clock;
    private final Duration resultTimeout;
    private final ConcurrentMap<UUID, RemoteClientSession> sessions = new ConcurrentHashMap<>();
    private final ConcurrentMap<RemoteOperationKey, PendingOperation> pendingOperations = new ConcurrentHashMap<>();

    @Autowired
    public MasterRemoteOperationGateway(Clock clock) {
        this(clock, DEFAULT_RESULT_TIMEOUT);
    }

    MasterRemoteOperationGateway(Clock clock, Duration resultTimeout) {
        this.clock = clock;
        this.resultTimeout = resultTimeout == null || resultTimeout.isZero() || resultTimeout.isNegative()
                ? DEFAULT_RESULT_TIMEOUT
                : resultTimeout;
    }

    public Duration resultTimeout() {
        return resultTimeout;
    }

    public void registerSession(
            ClientConnectionSnapshot snapshot,
            StreamObserver<MasterEnvelope> responseObserver) {
        if (snapshot == null || snapshot.clientNetworkIdentityId() == null || snapshot.connectionId() == null
                || responseObserver == null) {
            return;
        }

        sessions.put(
                snapshot.clientNetworkIdentityId(),
                new RemoteClientSession(
                        snapshot.clientNetworkIdentityId(),
                        snapshot.connectionId(),
                        responseObserver));
    }

    public Optional<DispatchHandle> dispatch(
            ClientConnectionSnapshot snapshot,
            OperationType operationType,
            String operationId,
            String targetDeviceId) {
        if (snapshot == null || snapshot.clientNetworkIdentityId() == null || snapshot.connectionId() == null) {
            return Optional.empty();
        }

        RemoteClientSession session = sessions.get(snapshot.clientNetworkIdentityId());
        if (session == null || !session.connectionId().equals(snapshot.connectionId())) {
            return Optional.empty();
        }

        RemoteOperationKey key = new RemoteOperationKey(targetDeviceId, operationId);
        PendingOperation pending = new PendingOperation(
                key,
                snapshot.clientNetworkIdentityId(),
                snapshot.connectionId(),
                operationType);
        PendingOperation previous = pendingOperations.putIfAbsent(key, pending);
        if (previous != null) {
            return Optional.of(new DispatchHandle(key, previous.completion()));
        }

        try {
            session.send(MasterEnvelope.newBuilder()
                    .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                    .setOperationRequest(OperationRequest.newBuilder()
                            .setOperationId(operationId)
                            .setOperationType(toNetworkOperationType(operationType))
                            .setTargetDeviceId(targetDeviceId)
                            .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                            .setSentAtUnixMs(clock.instant().toEpochMilli())
                            .setTimeoutMs(resultTimeout.toMillis())
                            .build())
                    .build());
        } catch (RuntimeException exception) {
            completeAndRemove(
                    pending,
                    RemoteOperationOutcome.unknown("Operation result is unknown after dispatch failure."));
        }

        return Optional.of(new DispatchHandle(key, pending.completion()));
    }

    public void handleAccepted(
            OperationAccepted accepted,
            UUID clientNetworkIdentityId,
            String connectionId) {
        RemoteOperationKey key = new RemoteOperationKey(accepted.getTargetDeviceId(), accepted.getOperationId());
        PendingOperation pending = pendingOperations.get(key);
        if (pending == null || !pending.matches(clientNetworkIdentityId, connectionId)
                || !sameOperationType(pending.operationType(), accepted.getOperationType())) {
            return;
        }

        if (accepted.getStatus() == OperationAcceptanceStatus.OPERATION_ACCEPTANCE_STATUS_REJECTED
                || accepted.getStatus() == OperationAcceptanceStatus.OPERATION_ACCEPTANCE_STATUS_UNSPECIFIED) {
            completeAndRemove(
                    pending,
                    RemoteOperationOutcome.failed(
                            ErrorCode.OPERATION_REJECTED,
                            "Agent rejected the operation."));
        }
    }

    public void handleResult(
            OperationResult result,
            UUID clientNetworkIdentityId,
            String connectionId) {
        RemoteOperationKey key = new RemoteOperationKey(result.getTargetDeviceId(), result.getOperationId());
        PendingOperation pending = pendingOperations.get(key);
        if (pending == null || !pending.matches(clientNetworkIdentityId, connectionId)
                || !sameOperationType(pending.operationType(), result.getOperationType())) {
            return;
        }

        completeAndRemove(pending, outcomeFrom(result));
    }

    public RemoteOperationOutcome timeout(DispatchHandle handle) {
        PendingOperation pending = pendingOperations.remove(handle.key());
        RemoteOperationOutcome outcome = RemoteOperationOutcome.unknown(
                "Operation result is unknown after timeout.");
        if (pending != null) {
            pending.completion().complete(outcome);
        }
        return outcome;
    }

    public void disconnect(UUID clientNetworkIdentityId, String connectionId, String reasonCode) {
        if (clientNetworkIdentityId == null || connectionId == null) {
            return;
        }

        sessions.computeIfPresent(clientNetworkIdentityId, (identityId, current) ->
                connectionId.equals(current.connectionId()) ? null : current);
        for (PendingOperation pending : pendingOperations.values()) {
            if (pending.matches(clientNetworkIdentityId, connectionId)) {
                completeAndRemove(
                        pending,
                        RemoteOperationOutcome.unknown("Operation result is unknown after disconnect."));
            }
        }
    }

    int pendingCount() {
        return pendingOperations.size();
    }

    private void completeAndRemove(PendingOperation pending, RemoteOperationOutcome outcome) {
        if (pendingOperations.remove(pending.key(), pending)) {
            pending.completion().complete(outcome);
        }
    }

    private RemoteOperationOutcome outcomeFrom(OperationResult result) {
        if (result.getStatus() == OperationExecutionStatus.OPERATION_EXECUTION_STATUS_SUCCESS) {
            return RemoteOperationOutcome.success("Agent reported operation success.");
        }

        return RemoteOperationOutcome.failed(
                errorCodeFrom(result.getErrorCode()),
                messageFor(result.getErrorCode(), result.getStatus()));
    }

    private ErrorCode errorCodeFrom(NetworkOperationErrorCode errorCode) {
        return switch (errorCode) {
            case NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_UNAVAILABLE -> ErrorCode.POWER_CONTROL_UNAVAILABLE;
            case NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_FAILED -> ErrorCode.POWER_CONTROL_FAILED;
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_NOT_IMPLEMENTED -> ErrorCode.OPERATION_NOT_IMPLEMENTED;
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_DUPLICATE -> ErrorCode.OPERATION_ALREADY_RUNNING;
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_REJECTED -> ErrorCode.OPERATION_REJECTED;
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_TIMEOUT,
                    NETWORK_OPERATION_ERROR_CODE_PROTOCOL_VIOLATION,
                    NETWORK_OPERATION_ERROR_CODE_UNSPECIFIED,
                    UNRECOGNIZED -> ErrorCode.OPERATION_RESULT_UNKNOWN;
        };
    }

    private String messageFor(
            NetworkOperationErrorCode errorCode,
            OperationExecutionStatus executionStatus) {
        return switch (errorCode) {
            case NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_UNAVAILABLE ->
                    "Power control is unavailable on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_FAILED ->
                    "Power control failed on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_NOT_IMPLEMENTED ->
                    "Operation is not implemented by the target Agent.";
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_DUPLICATE ->
                    "Operation is already running on the target Agent.";
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_REJECTED ->
                    "Agent rejected the operation.";
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_TIMEOUT ->
                    "Agent reported operation timeout.";
            case NETWORK_OPERATION_ERROR_CODE_PROTOCOL_VIOLATION ->
                    "Agent reported an operation protocol violation.";
            case NETWORK_OPERATION_ERROR_CODE_UNSPECIFIED,
                    UNRECOGNIZED -> executionStatus == OperationExecutionStatus.OPERATION_EXECUTION_STATUS_TIMED_OUT
                            ? "Agent reported operation timeout."
                            : "Agent reported operation failure.";
        };
    }

    private NetworkOperationType toNetworkOperationType(OperationType operationType) {
        return switch (operationType) {
            case SHUTDOWN -> NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN;
            case RESTART -> NetworkOperationType.NETWORK_OPERATION_TYPE_RESTART;
            default -> NetworkOperationType.NETWORK_OPERATION_TYPE_UNSPECIFIED;
        };
    }

    private boolean sameOperationType(OperationType operationType, NetworkOperationType networkOperationType) {
        return toNetworkOperationType(operationType) == networkOperationType;
    }

    public record DispatchHandle(
            RemoteOperationKey key,
            CompletableFuture<RemoteOperationOutcome> completion) {
    }

    public record RemoteOperationOutcome(
            TargetExecutionStatus status,
            ErrorCode errorCode,
            String message) {

        public static RemoteOperationOutcome success(String message) {
            return new RemoteOperationOutcome(TargetExecutionStatus.SUCCESS, null, message);
        }

        public static RemoteOperationOutcome failed(ErrorCode errorCode, String message) {
            return new RemoteOperationOutcome(TargetExecutionStatus.FAILED, errorCode, message);
        }

        public static RemoteOperationOutcome unknown(String message) {
            return failed(ErrorCode.OPERATION_RESULT_UNKNOWN, message);
        }
    }

    public record RemoteOperationKey(String deviceId, String operationId) {
    }

    private record RemoteClientSession(
            UUID clientNetworkIdentityId,
            String connectionId,
            StreamObserver<MasterEnvelope> responseObserver) {

        synchronized void send(MasterEnvelope envelope) {
            responseObserver.onNext(envelope);
        }
    }

    private record PendingOperation(
            RemoteOperationKey key,
            UUID clientNetworkIdentityId,
            String connectionId,
            OperationType operationType,
            CompletableFuture<RemoteOperationOutcome> completion) {

        PendingOperation(
                RemoteOperationKey key,
                UUID clientNetworkIdentityId,
                String connectionId,
                OperationType operationType) {
            this(key, clientNetworkIdentityId, connectionId, operationType, new CompletableFuture<>());
        }

        boolean matches(UUID clientNetworkIdentityId, String connectionId) {
            return this.clientNetworkIdentityId.equals(clientNetworkIdentityId)
                    && this.connectionId.equals(connectionId);
        }
    }
}
