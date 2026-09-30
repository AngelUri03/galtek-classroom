package com.galtek.classroom.windows;

import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.activity.DeviceActivityRecorder;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientConnectionSnapshot;
import com.galtek.classroom.network.MasterRemoteOperationGateway;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
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
import com.galtek.classroom.security.SensitiveActionAuthorizationController;
import com.galtek.classroom.windows.WindowsSessionLogoffDtos.WindowsSessionLogoffBatchResponse;
import com.galtek.classroom.windows.WindowsSessionLogoffDtos.WindowsSessionLogoffTargetResponse;
import com.galtek.classroom.windows.WindowsSessionStateDtos.WindowsSessionStateBatchResponse;
import com.galtek.classroom.windows.WindowsSessionStateDtos.WindowsSessionStateTargetResponse;
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
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled",
        havingValue = "true", matchIfMissing = true)
public class WindowsSessionLogoffService {
    private static final Set<String> REQUEST_FIELDS = Set.of("accountId", "targetDeviceIds");
    private static final String REQUESTED_BY = "LOCAL_MASTER";
    private static final int MAX_TARGET_DEVICES = 100;

    private final WindowsSessionStateReadService stateReadService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway gateway;
    private final BatchOperationService batchOperationService;
    private final Clock clock;
    private final MasterAccessGuard accessGuard;
    private final DeviceActivityRecorder activityRecorder;

    public WindowsSessionLogoffService(
            WindowsSessionStateReadService stateReadService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway gateway,
            BatchOperationService batchOperationService,
            Clock clock,
            MasterAccessGuard accessGuard,
            DeviceActivityRecorder activityRecorder) {
        this.stateReadService = stateReadService;
        this.connectionRegistry = connectionRegistry;
        this.gateway = gateway;
        this.batchOperationService = batchOperationService;
        this.clock = clock;
        this.accessGuard = accessGuard;
        this.activityRecorder = activityRecorder;
    }

    public WindowsSessionLogoffBatchResponse logoff(String classroomId, Map<String, Object> request) {
        MasterAuthorizationResponse authorization = accessGuard.requireAuthorized();
        String actor = SensitiveActionAuthorizationController.actorId(authorization);
        Request parsed = parse(request);
        String operationId = UUID.randomUUID().toString();
        OffsetDateTime createdAtUtc = OffsetDateTime.ofInstant(clock.instant(), ZoneOffset.UTC);
        WindowsSessionStateBatchResponse states = stateReadService.read(
                classroomId, Map.of("targetDeviceIds", parsed.targetDeviceIds()));
        Map<String, WindowsSessionLogoffTargetResponse> results = new LinkedHashMap<>();
        Map<String, ClientConnectionSnapshot> ready = new LinkedHashMap<>();
        Map<String, DispatchHandle> dispatched = new LinkedHashMap<>();

        for (WindowsSessionStateTargetResponse state : states.targets()) {
            if (state.errorCode() != null) {
                results.put(state.deviceId(), failed(state.deviceId(), state.errorCode(), state.message()));
                continue;
            }
            WindowsSessionState observed = WindowsSessionState.valueOf(state.state());
            if (observed == WindowsSessionState.NO_SESSION) {
                results.put(state.deviceId(), new WindowsSessionLogoffTargetResponse(
                        state.deviceId(), TargetExecutionStatus.NO_CHANGE.name(), null,
                        "Windows session is already absent."));
                continue;
            }
            if (observed == WindowsSessionState.UNKNOWN) {
                results.put(state.deviceId(), failed(state.deviceId(), ErrorCode.WINDOWS_SESSION_UNKNOWN.name(),
                        "Windows session state is unknown."));
                continue;
            }
            if (!observed.matches(parsed.accountId())) {
                results.put(state.deviceId(), failed(state.deviceId(), ErrorCode.WINDOWS_SESSION_CHANGED.name(),
                        "Windows session does not match the requested managed profile."));
                continue;
            }
            ClientConnectionSnapshot snapshot = connectionRegistry.findByDeviceId(state.deviceId()).orElse(null);
            if (snapshot == null || !snapshot.capabilities().contains(DeviceCapability.WINDOWS_SESSION_LOGOFF_V1)) {
                results.put(state.deviceId(), failed(state.deviceId(), ErrorCode.CAPABILITY_NOT_SUPPORTED.name(),
                        "Device does not announce WINDOWS_SESSION_LOGOFF_V1."));
                continue;
            }
            ready.put(state.deviceId(), snapshot);
            activityRecorder.record(classroomId, state.deviceId(), "WINDOWS_SESSION_LOGOFF_REQUESTED",
                    actor, parsed.accountId().name(), null, "REQUESTED", null);
        }

        List<BatchTargetResult> intentResults = parsed.targetDeviceIds().stream()
                .map(deviceId -> {
                    WindowsSessionLogoffTargetResponse result = results.get(deviceId);
                    return result == null
                            ? pending(deviceId)
                            : batchResult(result);
                })
                .toList();
        batchOperationService.create(
                classroomId,
                BatchOperation.fromTargets(
                        operationId,
                        OperationType.LOGOFF_WINDOWS_SESSION,
                        REQUESTED_BY,
                        createdAtUtc,
                        intentResults),
                payloadFor(parsed.accountId()));

        for (Map.Entry<String, ClientConnectionSnapshot> entry : ready.entrySet()) {
            String deviceId = entry.getKey();
            gateway.logoffWindowsSession(entry.getValue(), operationId + ":" + deviceId, deviceId,
                            networkAccountId(parsed.accountId()))
                    .ifPresentOrElse(handle -> dispatched.put(deviceId, handle),
                            () -> results.put(deviceId, failed(deviceId,
                                    ErrorCode.DEVICE_OFFLINE.name(), "Device is offline.")));
        }

        long deadline = System.nanoTime() + gateway.resultTimeout().toNanos();
        for (Map.Entry<String, DispatchHandle> entry : dispatched.entrySet()) {
            RemoteOperationOutcome outcome = await(entry.getValue(), deadline);
            results.put(entry.getKey(), new WindowsSessionLogoffTargetResponse(
                    entry.getKey(), outcome.status().name(),
                    outcome.errorCode() == null ? null : outcome.errorCode().name(), outcome.message()));
            activityRecorder.record(classroomId, entry.getKey(),
                    outcome.status() == TargetExecutionStatus.SUCCESS
                            ? "WINDOWS_SESSION_LOGOFF_SUCCEEDED"
                            : "WINDOWS_SESSION_LOGOFF_FAILED",
                    actor, parsed.accountId().name(), null, outcome.status().name(), outcome.message());
        }
        for (String deviceId : ready.keySet()) {
            if (dispatched.containsKey(deviceId)) continue;
            WindowsSessionLogoffTargetResponse failed = results.get(deviceId);
            if (failed != null) {
                activityRecorder.record(classroomId, deviceId, "WINDOWS_SESSION_LOGOFF_FAILED",
                        actor, parsed.accountId().name(), null, failed.status(), failed.message());
            }
        }

        List<WindowsSessionLogoffTargetResponse> ordered = parsed.targetDeviceIds().stream()
                .map(results::get).toList();
        BatchOperation completed = BatchOperation.fromTargets(
                operationId,
                OperationType.LOGOFF_WINDOWS_SESSION,
                REQUESTED_BY,
                createdAtUtc,
                ordered.stream().map(WindowsSessionLogoffService::batchResult).toList());
        batchOperationService.replaceResults(completed);
        return new WindowsSessionLogoffBatchResponse(operationId, "LOGOFF_WINDOWS_SESSION",
                parsed.accountId().name(), completed.status().name(), ordered.size(), ordered);
    }

    private RemoteOperationOutcome await(DispatchHandle handle, long deadline) {
        long remaining = deadline - System.nanoTime();
        if (remaining <= 0) return gateway.timeout(handle);
        try {
            return handle.completion().get(remaining, TimeUnit.NANOSECONDS);
        } catch (TimeoutException | ExecutionException exception) {
            return gateway.timeout(handle);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return gateway.timeout(handle);
        }
    }

    private Request parse(Map<String, Object> request) {
        if (request == null) throw invalid("Request body is required.");
        for (String field : request.keySet()) if (!REQUEST_FIELDS.contains(field)) {
            throw invalid("Request contains unsupported field: " + field + ".");
        }
        Object rawAccountId = request.get("accountId");
        ManagedWindowsAccountType accountId;
        try {
            accountId = ManagedWindowsAccountType.valueOf(rawAccountId instanceof String value ? value.trim() : "");
        } catch (IllegalArgumentException exception) {
            throw invalid("accountId must be PRIMARY, SECONDARY or ADMIN.");
        }
        Object rawTargets = request.get("targetDeviceIds");
        if (!(rawTargets instanceof List<?> list) || list.isEmpty() || list.size() > MAX_TARGET_DEVICES) {
            throw invalid("targetDeviceIds is required and must contain between 1 and 100 devices.");
        }
        List<String> targets = new ArrayList<>();
        Set<String> unique = new LinkedHashSet<>();
        for (Object raw : list) {
            if (!(raw instanceof String value) || value.isBlank() || !unique.add(value.trim())) {
                throw invalid("targetDeviceIds must contain unique non-empty strings.");
            }
            targets.add(value.trim());
        }
        return new Request(accountId, List.copyOf(targets));
    }

    private static ManagedWindowsAccountId networkAccountId(ManagedWindowsAccountType type) {
        return switch (type) {
            case PRIMARY -> ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY;
            case SECONDARY -> ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY;
            case ADMIN -> ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_ADMIN;
        };
    }

    private static WindowsSessionLogoffTargetResponse failed(String deviceId, String errorCode, String message) {
        return new WindowsSessionLogoffTargetResponse(deviceId, TargetExecutionStatus.FAILED.name(), errorCode, message);
    }

    private static BatchTargetResult pending(String deviceId) {
        return new BatchTargetResult(target(deviceId), TargetExecutionStatus.PENDING, null, "Dispatch pending.", 1);
    }

    private static BatchTargetResult batchResult(WindowsSessionLogoffTargetResponse result) {
        return new BatchTargetResult(
                target(result.deviceId()),
                TargetExecutionStatus.valueOf(result.status()),
                result.errorCode() == null ? null : ErrorCode.valueOf(result.errorCode()),
                result.message(),
                1);
    }

    private static OperationTarget target(String deviceId) {
        return new OperationTarget(OperationTargetType.DEVICE, deviceId, deviceId);
    }

    private static OperationPayload payloadFor(ManagedWindowsAccountType accountId) {
        return new OperationPayload(1, "{\"schemaVersion\":1,\"accountId\":\"" + accountId.name() + "\"}");
    }

    private static ApiException invalid(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, message);
    }

    private record Request(ManagedWindowsAccountType accountId, List<String> targetDeviceIds) {
    }
}
