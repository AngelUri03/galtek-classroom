package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceStatus;
import java.util.List;

public final class BatchOperationPlanner {

    public BatchPreflightPlan planForDevices(
            String operationId,
            OperationType operationType,
            List<Device> devices) {
        operationId = requireNonBlank(operationId, "operationId");
        requireNonNull(operationType, "operationType");
        devices = copyList(devices, "devices");

        var targets = devices.stream()
                .map(device -> new BatchPreflightItem(
                        new OperationTarget(OperationTargetType.DEVICE, device.deviceId(), device.displayName()),
                        statusFor(device.status()),
                        errorFor(device.status()),
                        messageFor(device.status())))
                .toList();

        return BatchPreflightPlan.fromTargets(operationId, operationType, targets);
    }

    public List<BatchTargetResult> retryableFailedTargets(BatchOperation operation) {
        requireNonNull(operation, "operation");

        return operation.retryableFailedTargets();
    }

    private static PreflightStatus statusFor(DeviceStatus deviceStatus) {
        return switch (deviceStatus) {
            case ONLINE -> PreflightStatus.READY;
            case CONNECTING, BUSY -> PreflightStatus.WARNING;
            case OFFLINE, UNLICENSED, LICENSE_BLOCKED, AGENT_UNAVAILABLE, SESSION_UNAVAILABLE, ERROR ->
                    PreflightStatus.BLOCKED;
        };
    }

    private static ErrorCode errorFor(DeviceStatus deviceStatus) {
        return deviceStatus == DeviceStatus.ONLINE ? null : deviceStatus.toOperationalError();
    }

    private static String messageFor(DeviceStatus deviceStatus) {
        return switch (deviceStatus) {
            case ONLINE -> null;
            case CONNECTING -> "Device is connecting.";
            case BUSY -> "Device is busy.";
            case OFFLINE -> "Device is offline.";
            case UNLICENSED -> "Device license is not active.";
            case LICENSE_BLOCKED -> "Device license is blocked.";
            case AGENT_UNAVAILABLE -> "Agent is unavailable.";
            case SESSION_UNAVAILABLE -> "Session is unavailable.";
            case ERROR -> "Device is in error state.";
        };
    }
}
