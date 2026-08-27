package com.galtek.classroom.windows;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchOperationStatus;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationTarget;
import com.galtek.classroom.operations.OperationTargetType;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.PreflightStatus;
import com.galtek.classroom.operations.TargetExecutionStatus;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;

class ManagedAccountSwitchPlannerTest {

    private final ManagedAccountSwitchPlanner planner = new ManagedAccountSwitchPlanner();

    @Test
    void targetAccountAlreadyActiveIsReadyNoChange() {
        var plan = planner.planSwitch(
                "OP-WIN-1",
                ManagedWindowsAccountType.PRIMARY,
                List.of(target("DEV-PC01", DeviceStatus.ONLINE, WindowsSessionState.PRIMARY_ACTIVE, primaryReady())));

        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.noChangeTargets()).hasSize(1);
        assertThat(plan.noChangeTargets().getFirst().action()).isEqualTo(ManagedAccountSwitchAction.NO_CHANGE);
        assertThat(plan.executableTargets()).isEmpty();
    }

    @Test
    void noSessionRequiresManagedLogon() {
        var plan = planner.planSwitch(
                "OP-WIN-2",
                ManagedWindowsAccountType.PRIMARY,
                List.of(target("DEV-PC03", DeviceStatus.ONLINE, WindowsSessionState.NO_SESSION, primaryReady())));

        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.executableTargets())
                .extracting(ManagedAccountSwitchPreflightItem::action)
                .containsExactly(ManagedAccountSwitchAction.LOGON);
    }

    @Test
    void otherManagedAccountActiveRequiresSwitch() {
        var plan = planner.planSwitch(
                "OP-WIN-3",
                ManagedWindowsAccountType.PRIMARY,
                List.of(target(
                        "DEV-PC02",
                        DeviceStatus.ONLINE,
                        WindowsSessionState.SECONDARY_ACTIVE,
                        primaryReady(),
                        secondaryReady())));

        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.executableTargets())
                .extracting(ManagedAccountSwitchPreflightItem::action)
                .containsExactly(ManagedAccountSwitchAction.SWITCH);
    }

    @Test
    void unavailableDeviceIsBlockedAsPendingWithExistingDeviceError() {
        var plan = planner.planSwitch(
                "OP-WIN-4",
                ManagedWindowsAccountType.PRIMARY,
                List.of(target("DEV-PC05", DeviceStatus.OFFLINE, WindowsSessionState.UNKNOWN, primaryReady())));

        assertThat(plan.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(plan.pendingTargets()).hasSize(1);
        assertThat(plan.blockedTargets().getFirst().errorCode()).isEqualTo(ErrorCode.DEVICE_OFFLINE);
    }

    @Test
    void missingAccountConfigurationBlocksTarget() {
        var plan = planner.planSwitch(
                "OP-WIN-5",
                ManagedWindowsAccountType.SECONDARY,
                List.of(target(
                        "DEV-PC04",
                        DeviceStatus.ONLINE,
                        WindowsSessionState.PRIMARY_ACTIVE,
                        primaryReady(),
                        ManagedWindowsAccount.notConfigured(ManagedWindowsAccountType.SECONDARY))));

        assertThat(plan.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(plan.blockedTargets().getFirst().errorCode()).isEqualTo(ErrorCode.ACCOUNT_NOT_CONFIGURED);
    }

    @Test
    void missingCredentialBlocksTargetWithoutPasswordMaterial() {
        var account = ManagedWindowsAccount.credentialMissing(
                ManagedWindowsAccountType.PRIMARY,
                "GALTEK-STUDENT-PRIMARY");

        var plan = planner.planSwitch(
                "OP-WIN-6",
                ManagedWindowsAccountType.PRIMARY,
                List.of(target("DEV-PC06", DeviceStatus.ONLINE, WindowsSessionState.NO_SESSION, account)));

        assertThat(account.accountId()).isEqualTo("PRIMARY");
        assertThat(account.accountReference()).isEqualTo("GALTEK-STUDENT-PRIMARY");
        assertThat(account.readyForManagedLogon()).isFalse();
        assertThat(plan.blockedTargets().getFirst().errorCode())
                .isEqualTo(ErrorCode.MANAGED_CREDENTIAL_NOT_CONFIGURED);
    }

    @Test
    void mixedClassroomSwitchPlanMatchesBatchFirstExample() {
        var plan = planner.planSwitch(
                "OP-WIN-7",
                ManagedWindowsAccountType.PRIMARY,
                List.of(
                        target("DEV-PC01", DeviceStatus.ONLINE, WindowsSessionState.PRIMARY_ACTIVE, primaryReady()),
                        target(
                                "DEV-PC02",
                                DeviceStatus.ONLINE,
                                WindowsSessionState.SECONDARY_ACTIVE,
                                primaryReady(),
                                secondaryReady()),
                        target("DEV-PC03", DeviceStatus.ONLINE, WindowsSessionState.NO_SESSION, primaryReady()),
                        target("DEV-PC04", DeviceStatus.ONLINE, WindowsSessionState.PRIMARY_ACTIVE, primaryReady()),
                        target("DEV-PC05", DeviceStatus.OFFLINE, WindowsSessionState.UNKNOWN, primaryReady())));

        assertThat(plan.status()).isEqualTo(PreflightStatus.WARNING);
        assertThat(plan.noChangeTargets())
                .extracting(item -> item.target().targetId())
                .containsExactly("DEV-PC01", "DEV-PC04");
        assertThat(plan.executableTargets())
                .extracting(ManagedAccountSwitchPreflightItem::action)
                .containsExactly(ManagedAccountSwitchAction.SWITCH, ManagedAccountSwitchAction.LOGON);
        assertThat(plan.pendingTargets())
                .extracting(item -> item.target().targetId())
                .containsExactly("DEV-PC05");
    }

    @Test
    void noChangeResultsAreSuccessfulAndNotRetried() {
        var operation = BatchOperation.fromTargets(
                "OP-WIN-8",
                OperationType.SWITCH_MANAGED_ACCOUNT,
                "LOCAL_MASTER",
                OffsetDateTime.parse("2026-08-26T12:00:00Z"),
                List.of(
                        result("DEV-PC01", TargetExecutionStatus.NO_CHANGE, null),
                        result("DEV-PC02", TargetExecutionStatus.SUCCESS, null),
                        result("DEV-PC05", TargetExecutionStatus.FAILED, ErrorCode.WINDOWS_LOGON_FAILED)));

        assertThat(operation.status()).isEqualTo(BatchOperationStatus.PARTIAL_SUCCESS);
        assertThat(operation.retryableFailedTargets())
                .extracting(item -> item.target().targetId())
                .containsExactly("DEV-PC05");
    }

    @Test
    void noChangeOnlyOperationIsSuccessful() {
        var operation = BatchOperation.fromTargets(
                "OP-WIN-9",
                OperationType.SWITCH_MANAGED_ACCOUNT,
                "LOCAL_MASTER",
                OffsetDateTime.parse("2026-08-26T12:00:00Z"),
                List.of(
                        result("DEV-PC01", TargetExecutionStatus.NO_CHANGE, null),
                        result("DEV-PC04", TargetExecutionStatus.NO_CHANGE, null)));

        assertThat(operation.status()).isEqualTo(BatchOperationStatus.SUCCESS);
        assertThat(operation.retryableFailedTargets()).isEmpty();
    }

    private static ManagedAccountSwitchTarget target(
            String deviceId,
            DeviceStatus deviceStatus,
            WindowsSessionState sessionState,
            ManagedWindowsAccount... accounts) {
        return new ManagedAccountSwitchTarget(
                new Device(
                        deviceId,
                        "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                        deviceId.replace("DEV-", ""),
                        deviceId.replace("DEV-", ""),
                        deviceStatus,
                        OffsetDateTime.parse("2026-08-26T12:00:00Z"),
                        Set.of(DeviceCapability.SESSION_AGENT),
                        null),
                sessionState,
                List.of(accounts));
    }

    private static ManagedWindowsAccount primaryReady() {
        return ManagedWindowsAccount.ready(ManagedWindowsAccountType.PRIMARY, "GALTEK-STUDENT-PRIMARY");
    }

    private static ManagedWindowsAccount secondaryReady() {
        return ManagedWindowsAccount.ready(ManagedWindowsAccountType.SECONDARY, "GALTEK-STUDENT-SECONDARY");
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
