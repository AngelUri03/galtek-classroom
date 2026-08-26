package com.galtek.classroom.operations;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceStatus;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;

class BatchOperationPlannerTest {

    private final BatchOperationPlanner planner = new BatchOperationPlanner();

    @Test
    void allAvailableDevicesAreReady() {
        var plan = planner.planForDevices(
                "OP-BATCH-1",
                OperationType.OPEN_APPLICATION,
                List.of(device("DEV-PC01", DeviceStatus.ONLINE), device("DEV-PC02", DeviceStatus.ONLINE)));

        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.readyTargets()).hasSize(2);
        assertThat(plan.warningTargets()).isEmpty();
        assertThat(plan.blockedTargets()).isEmpty();
    }

    @Test
    void mixedStatesAreClassifiedPerTarget() {
        var plan = planner.planForDevices(
                "OP-BATCH-2",
                OperationType.DISTRIBUTE_FILE,
                List.of(
                        device("DEV-PC01", DeviceStatus.ONLINE),
                        device("DEV-PC02", DeviceStatus.CONNECTING),
                        device("DEV-PC03", DeviceStatus.OFFLINE)));

        assertThat(plan.status()).isEqualTo(PreflightStatus.WARNING);
        assertThat(plan.readyTargets()).hasSize(1);
        assertThat(plan.warningTargets()).hasSize(1);
        assertThat(plan.blockedTargets()).hasSize(1);
        assertThat(plan.blockedTargets().getFirst().errorCode()).isEqualTo(ErrorCode.DEVICE_OFFLINE);
    }

    @Test
    void partialAvailabilityKeepsExecutableReadyTargets() {
        var plan = planner.planForDevices(
                "OP-BATCH-3",
                OperationType.SET_WALLPAPER,
                List.of(
                        device("DEV-PC01", DeviceStatus.ONLINE),
                        device("DEV-PC02", DeviceStatus.SESSION_UNAVAILABLE)));

        assertThat(plan.status()).isEqualTo(PreflightStatus.WARNING);
        assertThat(plan.readyTargets())
                .extracting(item -> item.target().targetId())
                .containsExactly("DEV-PC01");
        assertThat(plan.blockedTargets())
                .extracting(BatchPreflightItem::errorCode)
                .containsExactly(ErrorCode.SESSION_NOT_AVAILABLE);
    }

    @Test
    void allWarningTargetsKeepWarningPreflightStatus() {
        var plan = planner.planForDevices(
                "OP-BATCH-4",
                OperationType.OPEN_URL,
                List.of(device("DEV-PC01", DeviceStatus.CONNECTING), device("DEV-PC02", DeviceStatus.BUSY)));

        assertThat(plan.status()).isEqualTo(PreflightStatus.WARNING);
        assertThat(plan.warningTargets()).hasSize(2);
    }

    @Test
    void retryTargetsPreserveOnlyFailedResults() {
        var operation = BatchOperation.fromTargets(
                "OP-BATCH-5",
                OperationType.OPEN_APPLICATION,
                "SID:S-1-5-21-1000",
                OffsetDateTime.parse("2026-08-25T15:00:00Z"),
                List.of(
                        result("DEV-PC01", TargetExecutionStatus.SUCCESS, null),
                        result("DEV-PC02", TargetExecutionStatus.FAILED, ErrorCode.DEVICE_OFFLINE),
                        result("DEV-PC03", TargetExecutionStatus.FAILED, ErrorCode.APPLICATION_NOT_INSTALLED),
                        result("DEV-PC04", TargetExecutionStatus.SUCCESS, null)));

        assertThat(operation.status()).isEqualTo(BatchOperationStatus.PARTIAL_SUCCESS);
        assertThat(operation.failedTargets())
                .extracting(result -> result.target().targetId())
                .containsExactly("DEV-PC02", "DEV-PC03");
        assertThat(planner.retryableFailedTargets(operation))
                .extracting(result -> result.target().targetId())
                .containsExactly("DEV-PC02");
    }

    private static Device device(String deviceId, DeviceStatus status) {
        return new Device(
                deviceId,
                "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                deviceId.replace("DEV-", ""),
                deviceId.replace("DEV-", ""),
                status,
                OffsetDateTime.parse("2026-08-25T15:00:00Z"),
                Set.of(DeviceCapability.SESSION_AGENT),
                null);
    }

    private static BatchTargetResult result(
            String deviceId,
            TargetExecutionStatus status,
            ErrorCode errorCode) {
        return new BatchTargetResult(
                new OperationTarget(OperationTargetType.DEVICE, deviceId, deviceId.replace("DEV-", "")),
                status,
                errorCode,
                null,
                1);
    }
}
