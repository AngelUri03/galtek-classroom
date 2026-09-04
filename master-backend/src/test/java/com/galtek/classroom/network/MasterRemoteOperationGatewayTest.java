package com.galtek.classroom.network;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.v1.ApplyBrowserDownloadPolicyOperationParameters;
import com.galtek.classroom.network.v1.ApplyBrowserPolicyOperationParameters;
import com.galtek.classroom.network.v1.BrowserDownloadRestrictionMode;
import com.galtek.classroom.network.v1.BrowserPolicyAccountScope;
import com.galtek.classroom.network.v1.BrowserPolicyMode;
import com.galtek.classroom.network.v1.BrowserPolicyRuleAction;
import com.galtek.classroom.network.v1.BrowserPolicyRuleMatchType;
import com.galtek.classroom.network.v1.BrowserPolicyRuleParameters;
import com.galtek.classroom.network.v1.MasterEnvelope;
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
import com.galtek.classroom.network.v1.WindowsSessionState;
import com.galtek.classroom.network.v1.WindowsSessionStateResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import io.grpc.stub.StreamObserver;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class MasterRemoteOperationGatewayTest {

    private static final Instant NOW = Instant.parse("2026-08-31T12:00:00Z");
    private static final Clock CLOCK = Clock.fixed(NOW, ZoneId.of("UTC"));

    @Test
    void dispatchSendsTypedOperationRequestWithBatchOperationId() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        RecordingObserver<MasterEnvelope> observer = new RecordingObserver<>();
        ClientConnectionSnapshot snapshot = snapshot("device-1", UUID.randomUUID(), "connection-1");
        gateway.registerSession(snapshot, observer);

        DispatchHandle handle = gateway.dispatch(snapshot, OperationType.SHUTDOWN, "batch-1", "device-1")
                .orElseThrow();

        assertThat(handle.completion()).isNotCompleted();
        assertThat(observer.values()).hasSize(1);
        OperationRequest request = observer.values().getFirst().getOperationRequest();
        assertThat(request.getOperationId()).isEqualTo("batch-1");
        assertThat(request.getTargetDeviceId()).isEqualTo("device-1");
        assertThat(request.getOperationType()).isEqualTo(NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN);
        assertThat(request.getProtocolVersion()).isEqualTo(MasterNetworkTransportConstants.PROTOCOL_VERSION);
        assertThat(request.getSentAtUnixMs()).isEqualTo(NOW.toEpochMilli());
        assertThat(request.getTimeoutMs()).isEqualTo(100);
        assertThat(gateway.pendingCount()).isEqualTo(1);
    }

    @Test
    void dispatchSendsInputControlOperationWithoutParameters() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        RecordingObserver<MasterEnvelope> observer = new RecordingObserver<>();
        ClientConnectionSnapshot snapshot = snapshot("device-1", UUID.randomUUID(), "connection-1");
        gateway.registerSession(snapshot, observer);

        gateway.dispatch(snapshot, OperationType.LOCK_INPUT, "batch-lock", "device-1").orElseThrow();
        gateway.dispatch(snapshot, OperationType.UNLOCK_INPUT, "batch-unlock", "device-1").orElseThrow();

        assertThat(observer.values()).hasSize(2);
        OperationRequest lockRequest = observer.values().get(0).getOperationRequest();
        OperationRequest unlockRequest = observer.values().get(1).getOperationRequest();
        assertThat(lockRequest.getOperationType()).isEqualTo(NetworkOperationType.NETWORK_OPERATION_TYPE_LOCK_INPUT);
        assertThat(unlockRequest.getOperationType()).isEqualTo(NetworkOperationType.NETWORK_OPERATION_TYPE_UNLOCK_INPUT);
        assertThat(lockRequest.hasOpenApplication()).isFalse();
        assertThat(lockRequest.hasOpenUrl()).isFalse();
        assertThat(lockRequest.hasApplyBrowserPolicy()).isFalse();
        assertThat(lockRequest.hasApplyBrowserDownloadPolicy()).isFalse();
        assertThat(unlockRequest.hasOpenApplication()).isFalse();
        assertThat(unlockRequest.hasOpenUrl()).isFalse();
        assertThat(unlockRequest.hasApplyBrowserPolicy()).isFalse();
        assertThat(unlockRequest.hasApplyBrowserDownloadPolicy()).isFalse();
    }

    @Test
    void dispatchSendsGetWindowsSessionStateWithoutParameters() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        RecordingObserver<MasterEnvelope> observer = new RecordingObserver<>();
        ClientConnectionSnapshot snapshot = snapshot("device-1", UUID.randomUUID(), "connection-1");
        gateway.registerSession(snapshot, observer);

        gateway.dispatch(snapshot, OperationType.GET_WINDOWS_SESSION_STATE, "batch-session", "device-1")
                .orElseThrow();

        OperationRequest request = observer.values().getFirst().getOperationRequest();
        assertThat(request.getOperationType())
                .isEqualTo(NetworkOperationType.NETWORK_OPERATION_TYPE_GET_WINDOWS_SESSION_STATE);
        assertThat(request.hasOpenApplication()).isFalse();
        assertThat(request.hasOpenUrl()).isFalse();
        assertThat(request.hasApplyBrowserPolicy()).isFalse();
        assertThat(request.hasApplyBrowserDownloadPolicy()).isFalse();
        assertThat(request.getAllFields().keySet())
                .extracting(field -> field.getJsonName())
                .doesNotContain("username", "sid", "sessionId", "accountId", "password");
    }

    @Test
    void dispatchSendsTypedBrowserNavigationPolicyParameters() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        RecordingObserver<MasterEnvelope> observer = new RecordingObserver<>();
        ClientConnectionSnapshot snapshot = snapshot("device-1", UUID.randomUUID(), "connection-1");
        ApplyBrowserPolicyOperationParameters parameters = ApplyBrowserPolicyOperationParameters.newBuilder()
                .setPolicyId("policy-1")
                .setPolicyVersion(7)
                .setImplicitUnrestricted(false)
                .setMode(BrowserPolicyMode.BROWSER_POLICY_MODE_ALLOWLIST)
                .setAccountScope(BrowserPolicyAccountScope.BROWSER_POLICY_ACCOUNT_SCOPE_ANY)
                .addRules(BrowserPolicyRuleParameters.newBuilder()
                        .setRuleId("rule-1")
                        .setAction(BrowserPolicyRuleAction.BROWSER_POLICY_RULE_ACTION_ALLOW)
                        .setMatchType(BrowserPolicyRuleMatchType.BROWSER_POLICY_RULE_MATCH_TYPE_HOST_EXACT)
                        .setPattern("example.edu")
                        .setEnabled(true)
                        .build())
                .build();
        gateway.registerSession(snapshot, observer);

        gateway.dispatch(
                        snapshot,
                        OperationType.APPLY_BROWSER_NAVIGATION_POLICY,
                        "batch-browser",
                        "device-1",
                        parameters)
                .orElseThrow();

        OperationRequest request = observer.values().getFirst().getOperationRequest();
        assertThat(request.getOperationType())
                .isEqualTo(NetworkOperationType.NETWORK_OPERATION_TYPE_APPLY_BROWSER_NAVIGATION_POLICY);
        assertThat(request.hasApplyBrowserPolicy()).isTrue();
        assertThat(request.hasApplyBrowserDownloadPolicy()).isFalse();
        assertThat(request.getApplyBrowserPolicy()).isEqualTo(parameters);
    }

    @Test
    void dispatchSendsTypedBrowserDownloadPolicyParameters() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        RecordingObserver<MasterEnvelope> observer = new RecordingObserver<>();
        ClientConnectionSnapshot snapshot = snapshot("device-1", UUID.randomUUID(), "connection-1");
        ApplyBrowserDownloadPolicyOperationParameters parameters =
                ApplyBrowserDownloadPolicyOperationParameters.newBuilder()
                        .setPolicyId("download-policy-1")
                        .setPolicyVersion(3)
                        .setImplicitNoSpecialRestrictions(false)
                        .setRestrictionMode(BrowserDownloadRestrictionMode
                                .BROWSER_DOWNLOAD_RESTRICTION_MODE_BLOCK_ALL)
                        .setAccountScope(BrowserPolicyAccountScope.BROWSER_POLICY_ACCOUNT_SCOPE_ANY)
                        .build();
        gateway.registerSession(snapshot, observer);

        gateway.dispatch(
                        snapshot,
                        OperationType.APPLY_BROWSER_DOWNLOAD_POLICY,
                        "batch-download",
                        "device-1",
                        parameters)
                .orElseThrow();

        OperationRequest request = observer.values().getFirst().getOperationRequest();
        assertThat(request.getOperationType())
                .isEqualTo(NetworkOperationType.NETWORK_OPERATION_TYPE_APPLY_BROWSER_DOWNLOAD_POLICY);
        assertThat(request.hasApplyBrowserDownloadPolicy()).isTrue();
        assertThat(request.hasApplyBrowserPolicy()).isFalse();
        assertThat(request.getApplyBrowserDownloadPolicy()).isEqualTo(parameters);
    }

    @Test
    void dispatchSendsTypedOpenUrlParameters() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        RecordingObserver<MasterEnvelope> observer = new RecordingObserver<>();
        ClientConnectionSnapshot snapshot = snapshot("device-1", UUID.randomUUID(), "connection-1");
        OpenUrlOperationParameters parameters = OpenUrlOperationParameters.newBuilder()
                .setUrl("https://example.edu/material#page-2")
                .build();
        gateway.registerSession(snapshot, observer);

        gateway.dispatch(
                        snapshot,
                        OperationType.OPEN_URL,
                        "batch-open-url",
                        "device-1",
                        parameters)
                .orElseThrow();

        OperationRequest request = observer.values().getFirst().getOperationRequest();
        assertThat(request.getOperationType()).isEqualTo(NetworkOperationType.NETWORK_OPERATION_TYPE_OPEN_URL);
        assertThat(request.hasOpenUrl()).isTrue();
        assertThat(request.hasApplyBrowserPolicy()).isFalse();
        assertThat(request.hasApplyBrowserDownloadPolicy()).isFalse();
        assertThat(request.getOpenUrl()).isEqualTo(parameters);
    }

    @Test
    void dispatchSendsTypedOpenApplicationParameters() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        RecordingObserver<MasterEnvelope> observer = new RecordingObserver<>();
        ClientConnectionSnapshot snapshot = snapshot("device-1", UUID.randomUUID(), "connection-1");
        OpenApplicationOperationParameters parameters = OpenApplicationOperationParameters.newBuilder()
                .setApplicationId("conejito-lector")
                .build();
        gateway.registerSession(snapshot, observer);

        gateway.dispatch(
                        snapshot,
                        OperationType.OPEN_APPLICATION,
                        "batch-open-application",
                        "device-1",
                        parameters)
                .orElseThrow();

        OperationRequest request = observer.values().getFirst().getOperationRequest();
        assertThat(request.getOperationType()).isEqualTo(NetworkOperationType.NETWORK_OPERATION_TYPE_OPEN_APPLICATION);
        assertThat(request.hasOpenApplication()).isTrue();
        assertThat(request.getOpenApplication()).isEqualTo(parameters);
        assertThat(request.hasOpenUrl()).isFalse();
        assertThat(request.hasApplyBrowserPolicy()).isFalse();
        assertThat(request.hasApplyBrowserDownloadPolicy()).isFalse();
        assertThat(request.getOpenApplication().getAllFields().keySet())
                .extracting(field -> field.getJsonName())
                .containsExactly("applicationId");
    }

    @Test
    void operationResultMapsApplicationErrorsWithoutTextParsing() {
        assertApplicationError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_APPLICATION_BINDINGS_INVALID,
                ErrorCode.APPLICATION_BINDINGS_INVALID,
                false);
        assertApplicationError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_APPLICATION_BINDING_NOT_FOUND,
                ErrorCode.APPLICATION_BINDING_NOT_FOUND,
                false);
        assertApplicationError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_APPLICATION_BINDING_INVALID,
                ErrorCode.APPLICATION_BINDING_INVALID,
                false);
        assertApplicationError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_APPLICATION_DISABLED,
                ErrorCode.APPLICATION_DISABLED,
                false);
        assertApplicationError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_APPLICATION_EXECUTABLE_NOT_FOUND,
                ErrorCode.APPLICATION_EXECUTABLE_NOT_FOUND,
                false);
        assertApplicationError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_APPLICATION_LAUNCH_FAILED,
                ErrorCode.APPLICATION_LAUNCH_FAILED,
                true);
    }

    @Test
    void operationResultMapsInputControlErrorsWithoutTextParsing() {
        RemoteOperationOutcome lockOutcome = MasterRemoteOperationGateway.outcomeFromResult(failed(
                "batch-lock",
                "PC01",
                NetworkOperationType.NETWORK_OPERATION_TYPE_LOCK_INPUT,
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_INPUT_LOCK_FAILED));
        RemoteOperationOutcome unlockOutcome = MasterRemoteOperationGateway.outcomeFromResult(failed(
                "batch-unlock",
                "PC01",
                NetworkOperationType.NETWORK_OPERATION_TYPE_UNLOCK_INPUT,
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_INPUT_UNLOCK_FAILED));

        assertThat(lockOutcome.status()).isEqualTo(TargetExecutionStatus.FAILED);
        assertThat(lockOutcome.errorCode()).isEqualTo(ErrorCode.INPUT_LOCK_FAILED);
        assertThat(unlockOutcome.status()).isEqualTo(TargetExecutionStatus.FAILED);
        assertThat(unlockOutcome.errorCode()).isEqualTo(ErrorCode.INPUT_UNLOCK_FAILED);
    }

    @Test
    void operationResultPreservesTypedWindowsSessionState() {
        RemoteOperationOutcome outcome = MasterRemoteOperationGateway.outcomeFromResult(OperationResult.newBuilder()
                .setOperationId("batch-session")
                .setOperationType(NetworkOperationType.NETWORK_OPERATION_TYPE_GET_WINDOWS_SESSION_STATE)
                .setTargetDeviceId("PC01")
                .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                .setStatus(OperationExecutionStatus.OPERATION_EXECUTION_STATUS_SUCCESS)
                .setWindowsSessionState(WindowsSessionStateResult.newBuilder()
                        .setState(WindowsSessionState.WINDOWS_SESSION_STATE_SECONDARY_ACTIVE)
                        .build())
                .build());

        assertThat(outcome.status()).isEqualTo(TargetExecutionStatus.SUCCESS);
        assertThat(outcome.errorCode()).isNull();
        assertThat(outcome.windowsSessionState())
                .isEqualTo(WindowsSessionState.WINDOWS_SESSION_STATE_SECONDARY_ACTIVE);
    }

    @Test
    void unspecifiedWindowsSessionStateIsNotValidSuccess() {
        RemoteOperationOutcome outcome = MasterRemoteOperationGateway.outcomeFromResult(OperationResult.newBuilder()
                .setOperationId("batch-session")
                .setOperationType(NetworkOperationType.NETWORK_OPERATION_TYPE_GET_WINDOWS_SESSION_STATE)
                .setTargetDeviceId("PC01")
                .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                .setStatus(OperationExecutionStatus.OPERATION_EXECUTION_STATUS_SUCCESS)
                .setWindowsSessionState(WindowsSessionStateResult.newBuilder()
                        .setState(WindowsSessionState.WINDOWS_SESSION_STATE_UNSPECIFIED)
                        .build())
                .build());

        assertThat(outcome.status()).isEqualTo(TargetExecutionStatus.FAILED);
        assertThat(outcome.errorCode()).isEqualTo(ErrorCode.WINDOWS_SESSION_UNKNOWN);
    }

    @Test
    void operationResultMapsWindowsSessionErrorsWithoutTextParsing() {
        RemoteOperationOutcome unknown = MasterRemoteOperationGateway.outcomeFromResult(failed(
                "batch-session",
                "PC01",
                NetworkOperationType.NETWORK_OPERATION_TYPE_GET_WINDOWS_SESSION_STATE,
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_WINDOWS_SESSION_UNKNOWN));
        RemoteOperationOutcome invalidBindings = MasterRemoteOperationGateway.outcomeFromResult(failed(
                "batch-session-invalid",
                "PC01",
                NetworkOperationType.NETWORK_OPERATION_TYPE_GET_WINDOWS_SESSION_STATE,
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_MANAGED_ACCOUNT_BINDINGS_INVALID));

        assertThat(unknown.errorCode()).isEqualTo(ErrorCode.WINDOWS_SESSION_UNKNOWN);
        assertThat(invalidBindings.errorCode()).isEqualTo(ErrorCode.MANAGED_ACCOUNT_BINDINGS_INVALID);
    }

    @Test
    void statusQueryDoesNotSupportOpenApplicationReconciliation() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        ClientConnectionSnapshot snapshot = snapshot("PC01", UUID.randomUUID(), "connection-1");
        gateway.registerSession(snapshot, new RecordingObserver<>());

        assertThat(gateway.queryStatus(
                        snapshot,
                        OperationType.OPEN_APPLICATION,
                        "batch-open-application",
                        "PC01"))
                .isEmpty();
        assertThat(gateway.pendingStatusQueryCount()).isZero();
    }

    @Test
    void sameBatchOperationIdIsCorrelatedPerDevice() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        UUID identityA = UUID.randomUUID();
        UUID identityB = UUID.randomUUID();
        ClientConnectionSnapshot pc01 = snapshot("PC01", identityA, "connection-a");
        ClientConnectionSnapshot pc02 = snapshot("PC02", identityB, "connection-b");
        gateway.registerSession(pc01, new RecordingObserver<>());
        gateway.registerSession(pc02, new RecordingObserver<>());

        DispatchHandle first = gateway.dispatch(pc01, OperationType.RESTART, "batch-abc", "PC01")
                .orElseThrow();
        DispatchHandle second = gateway.dispatch(pc02, OperationType.RESTART, "batch-abc", "PC02")
                .orElseThrow();

        gateway.handleResult(success("batch-abc", "PC02", NetworkOperationType.NETWORK_OPERATION_TYPE_RESTART),
                identityB, "connection-b");
        assertThat(second.completion()).isCompletedWithValueMatching(
                outcome -> outcome.status() == TargetExecutionStatus.SUCCESS);
        assertThat(first.completion()).isNotCompleted();

        gateway.handleResult(failed(
                        "batch-abc",
                        "PC01",
                        NetworkOperationType.NETWORK_OPERATION_TYPE_RESTART,
                        NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_FAILED),
                identityA,
                "connection-a");
        assertThat(first.completion()).isCompletedWithValueMatching(
                outcome -> outcome.errorCode() == ErrorCode.POWER_CONTROL_FAILED);
        assertThat(gateway.pendingCount()).isZero();
    }

    @Test
    void operationAcceptedDoesNotCompleteAsSuccess() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        UUID identity = UUID.randomUUID();
        ClientConnectionSnapshot snapshot = snapshot("PC01", identity, "connection-1");
        gateway.registerSession(snapshot, new RecordingObserver<>());
        DispatchHandle handle = gateway.dispatch(snapshot, OperationType.SHUTDOWN, "batch-1", "PC01")
                .orElseThrow();

        gateway.handleAccepted(OperationAccepted.newBuilder()
                        .setOperationId("batch-1")
                        .setOperationType(NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN)
                        .setTargetDeviceId("PC01")
                        .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                        .setStatus(OperationAcceptanceStatus.OPERATION_ACCEPTANCE_STATUS_ACCEPTED)
                        .build(),
                identity,
                "connection-1");

        assertThat(handle.completion()).isNotCompleted();
        RemoteOperationOutcome timeout = gateway.timeout(handle);
        assertThat(timeout.status()).isEqualTo(TargetExecutionStatus.FAILED);
        assertThat(timeout.errorCode()).isEqualTo(ErrorCode.OPERATION_RESULT_UNKNOWN);
        assertThat(gateway.pendingCount()).isZero();
    }

    @Test
    void operationResultMapsPowerControlErrorsWithoutTextParsing() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        UUID identity = UUID.randomUUID();
        ClientConnectionSnapshot snapshot = snapshot("PC01", identity, "connection-1");
        gateway.registerSession(snapshot, new RecordingObserver<>());
        DispatchHandle unavailable = gateway.dispatch(snapshot, OperationType.SHUTDOWN, "batch-unavailable", "PC01")
                .orElseThrow();

        gateway.handleResult(failed(
                        "batch-unavailable",
                        "PC01",
                        NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN,
                        NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_POWER_CONTROL_UNAVAILABLE),
                identity,
                "connection-1");

        assertThat(unavailable.completion()).isCompletedWithValueMatching(outcome ->
                outcome.status() == TargetExecutionStatus.FAILED
                        && outcome.errorCode() == ErrorCode.POWER_CONTROL_UNAVAILABLE
                        && !outcome.errorCode().retryable());

        DispatchHandle notImplemented = gateway.dispatch(snapshot, OperationType.RESTART, "batch-not-impl", "PC01")
                .orElseThrow();
        gateway.handleResult(failed(
                        "batch-not-impl",
                        "PC01",
                        NetworkOperationType.NETWORK_OPERATION_TYPE_RESTART,
                        NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_OPERATION_NOT_IMPLEMENTED),
                identity,
                "connection-1");

        assertThat(notImplemented.completion()).isCompletedWithValueMatching(outcome ->
                outcome.errorCode() == ErrorCode.OPERATION_NOT_IMPLEMENTED
                        && !outcome.errorCode().retryable());
    }

    @Test
    void operationResultMapsBrowserDownloadPolicyErrors() {
        assertDownloadError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_INVALID,
                ErrorCode.BROWSER_DOWNLOAD_POLICY_INVALID,
                false);
        assertDownloadError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT,
                ErrorCode.BROWSER_DOWNLOAD_POLICY_EXTERNAL_CONFLICT,
                false);
        assertDownloadError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_APPLY_FAILED,
                ErrorCode.BROWSER_DOWNLOAD_POLICY_APPLY_FAILED,
                true);
        assertDownloadError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_ROLLBACK_FAILED,
                ErrorCode.BROWSER_DOWNLOAD_POLICY_ROLLBACK_FAILED,
                false);
        assertDownloadError(
                NetworkOperationErrorCode.NETWORK_OPERATION_ERROR_CODE_BROWSER_DOWNLOAD_POLICY_RECOVERY_REQUIRED,
                ErrorCode.BROWSER_DOWNLOAD_POLICY_RECOVERY_REQUIRED,
                false);
    }

    @Test
    void pendingMapIsCleanedAfterResultTimeoutRejectAndDisconnect() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        UUID identity = UUID.randomUUID();
        ClientConnectionSnapshot snapshot = snapshot("PC01", identity, "connection-1");
        gateway.registerSession(snapshot, new RecordingObserver<>());

        DispatchHandle success = gateway.dispatch(snapshot, OperationType.SHUTDOWN, "success", "PC01")
                .orElseThrow();
        gateway.handleResult(success("success", "PC01", NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN),
                identity, "connection-1");
        assertThat(success.completion()).isCompleted();
        assertThat(gateway.pendingCount()).isZero();

        DispatchHandle timeout = gateway.dispatch(snapshot, OperationType.SHUTDOWN, "timeout", "PC01")
                .orElseThrow();
        gateway.timeout(timeout);
        assertThat(gateway.pendingCount()).isZero();

        DispatchHandle rejected = gateway.dispatch(snapshot, OperationType.SHUTDOWN, "rejected", "PC01")
                .orElseThrow();
        gateway.handleAccepted(OperationAccepted.newBuilder()
                        .setOperationId("rejected")
                        .setOperationType(NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN)
                        .setTargetDeviceId("PC01")
                        .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                        .setStatus(OperationAcceptanceStatus.OPERATION_ACCEPTANCE_STATUS_REJECTED)
                        .build(),
                identity,
                "connection-1");
        assertThat(rejected.completion()).isCompletedWithValueMatching(
                outcome -> outcome.errorCode() == ErrorCode.OPERATION_REJECTED);
        assertThat(gateway.pendingCount()).isZero();

        DispatchHandle disconnected = gateway.dispatch(snapshot, OperationType.RESTART, "disconnect", "PC01")
                .orElseThrow();
        gateway.disconnect(identity, "connection-1", "TEST_DISCONNECT");
        assertThat(disconnected.completion()).isCompletedWithValueMatching(
                outcome -> outcome.errorCode() == ErrorCode.OPERATION_RESULT_UNKNOWN);
        assertThat(gateway.pendingCount()).isZero();
    }

    @Test
    void lateResultAfterTimeoutIsIgnored() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        UUID identity = UUID.randomUUID();
        ClientConnectionSnapshot snapshot = snapshot("PC01", identity, "connection-1");
        gateway.registerSession(snapshot, new RecordingObserver<>());
        DispatchHandle handle = gateway.dispatch(snapshot, OperationType.SHUTDOWN, "late", "PC01")
                .orElseThrow();

        RemoteOperationOutcome timeout = gateway.timeout(handle);
        gateway.handleResult(success("late", "PC01", NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN),
                identity, "connection-1");

        assertThat(timeout.errorCode()).isEqualTo(ErrorCode.OPERATION_RESULT_UNKNOWN);
        assertThat(handle.completion()).isCompletedWithValue(timeout);
        assertThat(gateway.pendingCount()).isZero();
    }

    @Test
    void statusQuerySendsReadOnlyOperationStatusQuery() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        RecordingObserver<MasterEnvelope> observer = new RecordingObserver<>();
        ClientConnectionSnapshot snapshot = snapshot("PC01", UUID.randomUUID(), "connection-1");
        gateway.registerSession(snapshot, observer);

        MasterRemoteOperationGateway.StatusQueryHandle handle = gateway.queryStatus(
                        snapshot,
                        OperationType.SHUTDOWN,
                        "batch-1",
                        "PC01")
                .orElseThrow();

        assertThat(handle.completion()).isNotCompleted();
        assertThat(observer.values()).hasSize(1);
        OperationStatusQuery query = observer.values().getFirst().getOperationStatusQuery();
        assertThat(query.getProtocolVersion()).isEqualTo(MasterNetworkTransportConstants.PROTOCOL_VERSION);
        assertThat(query.getOperationId()).isEqualTo("batch-1");
        assertThat(query.getTargetDeviceId()).isEqualTo("PC01");
        assertThat(gateway.pendingStatusQueryCount()).isEqualTo(1);
    }

    @Test
    void knownStatusReportCompletesStatusQueryWithoutCreatingOperationRequest() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        RecordingObserver<MasterEnvelope> observer = new RecordingObserver<>();
        UUID identity = UUID.randomUUID();
        ClientConnectionSnapshot snapshot = snapshot("PC01", identity, "connection-1");
        gateway.registerSession(snapshot, observer);
        MasterRemoteOperationGateway.StatusQueryHandle handle = gateway.queryStatus(
                        snapshot,
                        OperationType.SHUTDOWN,
                        "batch-1",
                        "PC01")
                .orElseThrow();

        gateway.handleStatusReport(OperationStatusReport.newBuilder()
                        .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                        .setOperationId("batch-1")
                        .setTargetDeviceId("PC01")
                        .setKnowledge(OperationStatusKnowledge.OPERATION_STATUS_KNOWLEDGE_KNOWN)
                        .setResult(success("batch-1", "PC01", NetworkOperationType.NETWORK_OPERATION_TYPE_SHUTDOWN))
                        .build(),
                identity,
                "connection-1");

        assertThat(handle.completion()).isCompletedWithValueMatching(outcome ->
                outcome.isPresent() && outcome.get().status() == TargetExecutionStatus.SUCCESS);
        assertThat(observer.values()).hasSize(1);
        assertThat(observer.values().getFirst().getPayloadCase())
                .isEqualTo(MasterEnvelope.PayloadCase.OPERATION_STATUS_QUERY);
        assertThat(gateway.pendingStatusQueryCount()).isZero();
    }

    @Test
    void unknownStatusReportAndTimeoutPreserveUnknownAndCleanMap() {
        MasterRemoteOperationGateway gateway = new MasterRemoteOperationGateway(CLOCK, Duration.ofMillis(100));
        UUID identity = UUID.randomUUID();
        ClientConnectionSnapshot snapshot = snapshot("PC01", identity, "connection-1");
        gateway.registerSession(snapshot, new RecordingObserver<>());
        MasterRemoteOperationGateway.StatusQueryHandle unknown = gateway.queryStatus(
                        snapshot,
                        OperationType.SHUTDOWN,
                        "unknown",
                        "PC01")
                .orElseThrow();

        gateway.handleStatusReport(OperationStatusReport.newBuilder()
                        .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                        .setOperationId("unknown")
                        .setTargetDeviceId("PC01")
                        .setKnowledge(OperationStatusKnowledge.OPERATION_STATUS_KNOWLEDGE_UNKNOWN)
                        .build(),
                identity,
                "connection-1");

        assertThat(unknown.completion()).isCompletedWithValue(java.util.Optional.empty());
        assertThat(gateway.pendingStatusQueryCount()).isZero();

        MasterRemoteOperationGateway.StatusQueryHandle timeout = gateway.queryStatus(
                        snapshot,
                        OperationType.SHUTDOWN,
                        "timeout",
                        "PC01")
                .orElseThrow();
        assertThat(gateway.timeoutStatusQuery(timeout)).isEmpty();
        assertThat(timeout.completion()).isCompletedWithValue(java.util.Optional.empty());
        assertThat(gateway.pendingStatusQueryCount()).isZero();
    }

    private static void assertDownloadError(
            NetworkOperationErrorCode networkError,
            ErrorCode expected,
            boolean retryable) {
        MasterRemoteOperationGateway.RemoteOperationOutcome outcome =
                MasterRemoteOperationGateway.outcomeFromResult(failed(
                        "download-policy",
                        "PC01",
                        NetworkOperationType.NETWORK_OPERATION_TYPE_APPLY_BROWSER_DOWNLOAD_POLICY,
                        networkError));

        assertThat(outcome.status()).isEqualTo(TargetExecutionStatus.FAILED);
        assertThat(outcome.errorCode()).isEqualTo(expected);
        assertThat(outcome.errorCode().retryable()).isEqualTo(retryable);
    }

    private static void assertApplicationError(
            NetworkOperationErrorCode networkError,
            ErrorCode expected,
            boolean retryable) {
        MasterRemoteOperationGateway.RemoteOperationOutcome outcome =
                MasterRemoteOperationGateway.outcomeFromResult(failed(
                        "open-application",
                        "PC01",
                        NetworkOperationType.NETWORK_OPERATION_TYPE_OPEN_APPLICATION,
                        networkError));

        assertThat(outcome.status()).isEqualTo(TargetExecutionStatus.FAILED);
        assertThat(outcome.errorCode()).isEqualTo(expected);
        assertThat(outcome.errorCode().retryable()).isEqualTo(retryable);
    }

    private static OperationResult success(
            String operationId,
            String deviceId,
            NetworkOperationType operationType) {
        return OperationResult.newBuilder()
                .setOperationId(operationId)
                .setOperationType(operationType)
                .setTargetDeviceId(deviceId)
                .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                .setStatus(OperationExecutionStatus.OPERATION_EXECUTION_STATUS_SUCCESS)
                .build();
    }

    private static OperationResult failed(
            String operationId,
            String deviceId,
            NetworkOperationType operationType,
            NetworkOperationErrorCode errorCode) {
        return OperationResult.newBuilder()
                .setOperationId(operationId)
                .setOperationType(operationType)
                .setTargetDeviceId(deviceId)
                .setProtocolVersion(MasterNetworkTransportConstants.PROTOCOL_VERSION)
                .setStatus(OperationExecutionStatus.OPERATION_EXECUTION_STATUS_FAILED)
                .setErrorCode(errorCode)
                .build();
    }

    private static ClientConnectionSnapshot snapshot(String deviceId, UUID identity, String connectionId) {
        return new ClientConnectionSnapshot(
                identity,
                UUID.randomUUID(),
                deviceId,
                "classroom-1",
                true,
                deviceId,
                deviceId.toLowerCase(),
                DeviceStatus.ONLINE,
                "0.5.0-test",
                Set.of(DeviceCapability.HEARTBEAT_V1, DeviceCapability.OPERATION_FRAMEWORK_V1,
                        DeviceCapability.POWER_CONTROL_V1),
                NOW,
                NOW,
                null,
                connectionId,
                null);
    }

    private static final class RecordingObserver<T> implements StreamObserver<T> {
        private final List<T> values = new ArrayList<>();

        @Override
        public void onNext(T value) {
            values.add(value);
        }

        @Override
        public void onError(Throwable throwable) {
        }

        @Override
        public void onCompleted() {
        }

        List<T> values() {
            return values;
        }
    }
}
