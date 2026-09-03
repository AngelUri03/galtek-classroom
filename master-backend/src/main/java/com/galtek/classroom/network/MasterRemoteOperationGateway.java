package com.galtek.classroom.network;

import com.galtek.classroom.network.v1.MasterEnvelope;
import com.galtek.classroom.network.v1.ApplyBrowserDownloadPolicyOperationParameters;
import com.galtek.classroom.network.v1.ApplyBrowserPolicyOperationParameters;
import com.galtek.classroom.network.v1.NetworkOperationErrorCode;
import com.galtek.classroom.network.v1.NetworkOperationType;
import com.galtek.classroom.network.v1.OpenApplicationOperationParameters;
import com.galtek.classroom.network.v1.OpenUrlOperationParameters;
import com.galtek.classroom.network.v1.OperationAcceptanceStatus;
import com.galtek.classroom.network.v1.OperationAccepted;
import com.galtek.classroom.network.v1.OperationExecutionStatus;
import com.galtek.classroom.network.v1.OperationRequest;
import com.galtek.classroom.network.v1.OperationResult;
import com.galtek.classroom.network.v1.OperationStatusKnowledge;
import com.galtek.classroom.network.v1.OperationStatusQuery;
import com.galtek.classroom.network.v1.OperationStatusReport;
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
    private static final Duration STATUS_QUERY_TIMEOUT = Duration.ofSeconds(2);

    private final Clock clock;
    private final Duration resultTimeout;
    private final ConcurrentMap<UUID, RemoteClientSession> sessions = new ConcurrentHashMap<>();
    private final ConcurrentMap<RemoteOperationKey, PendingOperation> pendingOperations = new ConcurrentHashMap<>();
    private final ConcurrentMap<RemoteOperationKey, PendingStatusQuery> pendingStatusQueries = new ConcurrentHashMap<>();

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

    public Duration statusQueryTimeout() {
        return STATUS_QUERY_TIMEOUT;
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
        return dispatch(snapshot, operationType, operationId, targetDeviceId, null, null, null, null);
    }

    public Optional<DispatchHandle> dispatch(
            ClientConnectionSnapshot snapshot,
            OperationType operationType,
            String operationId,
            String targetDeviceId,
            ApplyBrowserPolicyOperationParameters parameters) {
        return dispatch(snapshot, operationType, operationId, targetDeviceId, parameters, null, null, null);
    }

    public Optional<DispatchHandle> dispatch(
            ClientConnectionSnapshot snapshot,
            OperationType operationType,
            String operationId,
            String targetDeviceId,
            ApplyBrowserDownloadPolicyOperationParameters parameters) {
        return dispatch(snapshot, operationType, operationId, targetDeviceId, null, parameters, null, null);
    }

    public Optional<DispatchHandle> dispatch(
            ClientConnectionSnapshot snapshot,
            OperationType operationType,
            String operationId,
            String targetDeviceId,
            OpenUrlOperationParameters parameters) {
        return dispatch(snapshot, operationType, operationId, targetDeviceId, null, null, null, parameters);
    }

    public Optional<DispatchHandle> dispatch(
            ClientConnectionSnapshot snapshot,
            OperationType operationType,
            String operationId,
            String targetDeviceId,
            OpenApplicationOperationParameters parameters) {
        return dispatch(snapshot, operationType, operationId, targetDeviceId, null, null, parameters, null);
    }

    private Optional<DispatchHandle> dispatch(
            ClientConnectionSnapshot snapshot,
            OperationType operationType,
            String operationId,
            String targetDeviceId,
            ApplyBrowserPolicyOperationParameters browserPolicyParameters,
            ApplyBrowserDownloadPolicyOperationParameters browserDownloadPolicyParameters,
            OpenApplicationOperationParameters openApplicationParameters,
            OpenUrlOperationParameters openUrlParameters) {
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
            OperationRequest.Builder request = OperationRequest.newBuilder()
                    .setOperationId(operationId)
                    .setOperationType(toNetworkOperationType(operationType))
                    .setTargetDeviceId(targetDeviceId)
                    .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                    .setSentAtUnixMs(clock.instant().toEpochMilli())
                    .setTimeoutMs(resultTimeout.toMillis());
            if (browserPolicyParameters != null) {
                request.setApplyBrowserPolicy(browserPolicyParameters);
            }
            if (browserDownloadPolicyParameters != null) {
                request.setApplyBrowserDownloadPolicy(browserDownloadPolicyParameters);
            }
            if (openApplicationParameters != null) {
                request.setOpenApplication(openApplicationParameters);
            }
            if (openUrlParameters != null) {
                request.setOpenUrl(openUrlParameters);
            }
            session.send(MasterEnvelope.newBuilder()
                    .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                    .setOperationRequest(request.build())
                    .build());
        } catch (RuntimeException exception) {
            completeAndRemove(
                    pending,
                    RemoteOperationOutcome.unknown("Operation result is unknown after dispatch failure."));
        }

        return Optional.of(new DispatchHandle(key, pending.completion()));
    }

    public Optional<StatusQueryHandle> queryStatus(
            ClientConnectionSnapshot snapshot,
            OperationType operationType,
            String operationId,
            String targetDeviceId) {
        if (snapshot == null || snapshot.clientNetworkIdentityId() == null || snapshot.connectionId() == null
                || !isPowerOperation(operationType)
                || operationId == null || operationId.isBlank()
                || targetDeviceId == null || targetDeviceId.isBlank()) {
            return Optional.empty();
        }

        RemoteClientSession session = sessions.get(snapshot.clientNetworkIdentityId());
        if (session == null || !session.connectionId().equals(snapshot.connectionId())) {
            return Optional.empty();
        }

        RemoteOperationKey key = new RemoteOperationKey(targetDeviceId, operationId);
        PendingStatusQuery pending = new PendingStatusQuery(
                key,
                snapshot.clientNetworkIdentityId(),
                snapshot.connectionId(),
                operationType);
        PendingStatusQuery previous = pendingStatusQueries.putIfAbsent(key, pending);
        if (previous != null) {
            return Optional.of(new StatusQueryHandle(key, previous.completion()));
        }

        try {
            session.send(MasterEnvelope.newBuilder()
                    .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                    .setOperationStatusQuery(OperationStatusQuery.newBuilder()
                            .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                            .setOperationId(operationId)
                            .setTargetDeviceId(targetDeviceId)
                            .build())
                    .build());
        } catch (RuntimeException exception) {
            completeAndRemoveStatusQuery(pending, Optional.empty());
        }

        return Optional.of(new StatusQueryHandle(key, pending.completion()));
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

    public boolean handleResult(
            OperationResult result,
            UUID clientNetworkIdentityId,
            String connectionId) {
        RemoteOperationKey key = new RemoteOperationKey(result.getTargetDeviceId(), result.getOperationId());
        PendingOperation pending = pendingOperations.get(key);
        if (pending == null || !pending.matches(clientNetworkIdentityId, connectionId)
                || !sameOperationType(pending.operationType(), result.getOperationType())) {
            return false;
        }

        completeAndRemove(pending, outcomeFromResult(result));
        return true;
    }

    public void handleStatusReport(
            OperationStatusReport report,
            UUID clientNetworkIdentityId,
            String connectionId) {
        RemoteOperationKey key = new RemoteOperationKey(report.getTargetDeviceId(), report.getOperationId());
        PendingStatusQuery pending = pendingStatusQueries.get(key);
        if (pending == null || !pending.matches(clientNetworkIdentityId, connectionId)) {
            return;
        }

        Optional<RemoteOperationOutcome> outcome = Optional.empty();
        if (report.getKnowledge() == OperationStatusKnowledge.OPERATION_STATUS_KNOWLEDGE_KNOWN
                && report.hasResult()
                && report.getResult().getOperationId().equals(report.getOperationId())
                && report.getResult().getTargetDeviceId().equals(report.getTargetDeviceId())
                && sameOperationType(pending.operationType(), report.getResult().getOperationType())) {
            outcome = Optional.of(outcomeFromResult(report.getResult()));
        }

        completeAndRemoveStatusQuery(pending, outcome);
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
        for (PendingStatusQuery pending : pendingStatusQueries.values()) {
            if (pending.matches(clientNetworkIdentityId, connectionId)) {
                completeAndRemoveStatusQuery(pending, Optional.empty());
            }
        }
    }

    int pendingCount() {
        return pendingOperations.size();
    }

    int pendingStatusQueryCount() {
        return pendingStatusQueries.size();
    }

    private void completeAndRemove(PendingOperation pending, RemoteOperationOutcome outcome) {
        if (pendingOperations.remove(pending.key(), pending)) {
            pending.completion().complete(outcome);
        }
    }

    public Optional<RemoteOperationOutcome> timeoutStatusQuery(StatusQueryHandle handle) {
        PendingStatusQuery pending = pendingStatusQueries.remove(handle.key());
        if (pending != null) {
            pending.completion().complete(Optional.empty());
        }
        return Optional.empty();
    }

    private void completeAndRemoveStatusQuery(
            PendingStatusQuery pending,
            Optional<RemoteOperationOutcome> outcome) {
        if (pendingStatusQueries.remove(pending.key(), pending)) {
            pending.completion().complete(outcome);
        }
    }

    public static RemoteOperationOutcome outcomeFromResult(OperationResult result) {
        if (result.getStatus() == OperationExecutionStatus.OPERATION_EXECUTION_STATUS_SUCCESS) {
            return RemoteOperationOutcome.success("Agent reported operation success.");
        }

        return RemoteOperationOutcome.failed(
                errorCodeFrom(result.getErrorCode()),
                messageFor(result.getErrorCode(), result.getStatus()));
    }

    private static ErrorCode errorCodeFrom(NetworkOperationErrorCode errorCode) {
        return switch (errorCode) {
            case NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_UNAVAILABLE -> ErrorCode.POWER_CONTROL_UNAVAILABLE;
            case NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_FAILED -> ErrorCode.POWER_CONTROL_FAILED;
            case NETWORK_OPERATION_ERROR_CODE_INVALID_URL -> ErrorCode.INVALID_URL;
            case NETWORK_OPERATION_ERROR_CODE_SESSION_AGENT_UNAVAILABLE -> ErrorCode.SESSION_AGENT_UNAVAILABLE;
            case NETWORK_OPERATION_ERROR_CODE_SESSION_CHANNEL_UNAUTHORIZED -> ErrorCode.SESSION_CHANNEL_UNAUTHORIZED;
            case NETWORK_OPERATION_ERROR_CODE_SESSION_CHANNEL_PROTOCOL_MISMATCH ->
                    ErrorCode.SESSION_CHANNEL_PROTOCOL_MISMATCH;
            case NETWORK_OPERATION_ERROR_CODE_SESSION_CHANNEL_INVALID_RESPONSE ->
                    ErrorCode.SESSION_CHANNEL_INVALID_RESPONSE;
            case NETWORK_OPERATION_ERROR_CODE_SESSION_COMMAND_RESULT_UNKNOWN -> ErrorCode.SESSION_COMMAND_RESULT_UNKNOWN;
            case NETWORK_OPERATION_ERROR_CODE_URL_LAUNCH_FAILED -> ErrorCode.URL_LAUNCH_FAILED;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_INVALID -> ErrorCode.BROWSER_POLICY_INVALID;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE ->
                    ErrorCode.BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_TOO_LARGE -> ErrorCode.BROWSER_POLICY_TOO_LARGE;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_USER_UNAVAILABLE ->
                    ErrorCode.BROWSER_POLICY_USER_UNAVAILABLE;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_ACCOUNT_SCOPE_UNRESOLVED ->
                    ErrorCode.BROWSER_ACCOUNT_SCOPE_UNRESOLVED;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_USER_HIVE_UNAVAILABLE ->
                    ErrorCode.BROWSER_POLICY_USER_HIVE_UNAVAILABLE;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_EXTERNAL_CONFLICT ->
                    ErrorCode.BROWSER_POLICY_EXTERNAL_CONFLICT;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_APPLY_FAILED -> ErrorCode.BROWSER_POLICY_APPLY_FAILED;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_ROLLBACK_FAILED ->
                    ErrorCode.BROWSER_POLICY_ROLLBACK_FAILED;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_RECOVERY_REQUIRED ->
                    ErrorCode.BROWSER_POLICY_RECOVERY_REQUIRED;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_INVALID ->
                    ErrorCode.BROWSER_DOWNLOAD_POLICY_INVALID;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT ->
                    ErrorCode.BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_APPLY_FAILED ->
                    ErrorCode.BROWSER_DOWNLOAD_POLICY_APPLY_FAILED;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_ROLLBACK_FAILED ->
                    ErrorCode.BROWSER_DOWNLOAD_POLICY_ROLLBACK_FAILED;
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_RECOVERY_REQUIRED ->
                    ErrorCode.BROWSER_DOWNLOAD_POLICY_RECOVERY_REQUIRED;
            case NETWORK_OPERATION_ERROR_CODE_URL_BLOCKED_BY_POLICY -> ErrorCode.URL_BLOCKED_BY_POLICY;
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_BINDINGS_INVALID -> ErrorCode.APPLICATION_BINDINGS_INVALID;
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_BINDING_NOT_FOUND -> ErrorCode.APPLICATION_BINDING_NOT_FOUND;
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_BINDING_INVALID -> ErrorCode.APPLICATION_BINDING_INVALID;
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_DISABLED -> ErrorCode.APPLICATION_DISABLED;
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_EXECUTABLE_NOT_FOUND ->
                    ErrorCode.APPLICATION_EXECUTABLE_NOT_FOUND;
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_LAUNCH_FAILED -> ErrorCode.APPLICATION_LAUNCH_FAILED;
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_NOT_IMPLEMENTED -> ErrorCode.OPERATION_NOT_IMPLEMENTED;
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_DUPLICATE -> ErrorCode.OPERATION_ALREADY_RUNNING;
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_REJECTED -> ErrorCode.OPERATION_REJECTED;
            case NETWORK_OPERATION_ERROR_CODE_OPERATION_TIMEOUT,
                    NETWORK_OPERATION_ERROR_CODE_PROTOCOL_VIOLATION,
                    NETWORK_OPERATION_ERROR_CODE_UNSPECIFIED,
                    UNRECOGNIZED -> ErrorCode.OPERATION_RESULT_UNKNOWN;
        };
    }

    private static String messageFor(
            NetworkOperationErrorCode errorCode,
            OperationExecutionStatus executionStatus) {
        return switch (errorCode) {
            case NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_UNAVAILABLE ->
                    "Power control is unavailable on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_FAILED ->
                    "Power control failed on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_INVALID_URL ->
                    "Agent rejected an invalid URL.";
            case NETWORK_OPERATION_ERROR_CODE_SESSION_AGENT_UNAVAILABLE ->
                    "Session Agent is unavailable on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_SESSION_CHANNEL_UNAUTHORIZED ->
                    "Session command channel authorization failed on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_SESSION_CHANNEL_PROTOCOL_MISMATCH ->
                    "Session command protocol mismatch on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_SESSION_CHANNEL_INVALID_RESPONSE ->
                    "Session Agent returned an invalid response.";
            case NETWORK_OPERATION_ERROR_CODE_SESSION_COMMAND_RESULT_UNKNOWN ->
                    "Session command result is unknown after dispatch.";
            case NETWORK_OPERATION_ERROR_CODE_URL_LAUNCH_FAILED ->
                    "Windows did not accept the URL launch request.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_INVALID ->
                    "Agent rejected an invalid browser navigation policy.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE ->
                    "Browser navigation policy cannot be safely enforced by native browser policy.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_TOO_LARGE ->
                    "Browser navigation policy exceeds native browser policy limits.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_USER_UNAVAILABLE ->
                    "Interactive Windows user is unavailable on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_ACCOUNT_SCOPE_UNRESOLVED ->
                    "Browser account scope is not resolved on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_USER_HIVE_UNAVAILABLE ->
                    "Interactive user policy hive is unavailable on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_EXTERNAL_CONFLICT ->
                    "Existing browser policy conflicts with Galtek enforcement.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_APPLY_FAILED ->
                    "Browser navigation policy apply failed on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_ROLLBACK_FAILED ->
                    "Browser navigation policy rollback failed on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_POLICY_RECOVERY_REQUIRED ->
                    "Browser navigation policy requires local recovery.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_INVALID ->
                    "Agent rejected an invalid browser download policy.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT ->
                    "Existing browser download policy conflicts with Galtek enforcement.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_APPLY_FAILED ->
                    "Browser download policy apply failed on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_ROLLBACK_FAILED ->
                    "Browser download policy rollback failed on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_RECOVERY_REQUIRED ->
                    "Browser download policy requires local recovery.";
            case NETWORK_OPERATION_ERROR_CODE_URL_BLOCKED_BY_POLICY ->
                    "URL is blocked by applied browser navigation policy.";
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_BINDINGS_INVALID ->
                    "Agent reported an invalid application binding catalog.";
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_BINDING_NOT_FOUND ->
                    "Application binding was not found on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_BINDING_INVALID ->
                    "Application binding is invalid on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_DISABLED ->
                    "Application binding is disabled on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_EXECUTABLE_NOT_FOUND ->
                    "Application executable was not found on the target device.";
            case NETWORK_OPERATION_ERROR_CODE_APPLICATION_LAUNCH_FAILED ->
                    "Windows did not accept the application launch request.";
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
            case OPEN_APPLICATION -> NetworkOperationType.NETWORK_OPERATION_TYPE_OPEN_APPLICATION;
            case OPEN_URL -> NetworkOperationType.NETWORK_OPERATION_TYPE_OPEN_URL;
            case APPLY_BROWSER_NAVIGATION_POLICY ->
                    NetworkOperationType.NETWORK_OPERATION_TYPE_APPLY_BROWSER_NAVIGATION_POLICY;
            case APPLY_BROWSER_DOWNLOAD_POLICY ->
                    NetworkOperationType.NETWORK_OPERATION_TYPE_APPLY_BROWSER_DOWNLOAD_POLICY;
            default -> NetworkOperationType.NETWORK_OPERATION_TYPE_UNSPECIFIED;
        };
    }

    private boolean sameOperationType(OperationType operationType, NetworkOperationType networkOperationType) {
        return toNetworkOperationType(operationType) == networkOperationType;
    }

    private boolean isPowerOperation(OperationType operationType) {
        return operationType == OperationType.SHUTDOWN || operationType == OperationType.RESTART;
    }

    public record DispatchHandle(
            RemoteOperationKey key,
            CompletableFuture<RemoteOperationOutcome> completion) {
    }

    public record StatusQueryHandle(
            RemoteOperationKey key,
            CompletableFuture<Optional<RemoteOperationOutcome>> completion) {
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

    private record PendingStatusQuery(
            RemoteOperationKey key,
            UUID clientNetworkIdentityId,
            String connectionId,
            OperationType operationType,
            CompletableFuture<Optional<RemoteOperationOutcome>> completion) {

        PendingStatusQuery(
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
