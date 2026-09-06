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
                List.of(target("DEV-PC01", DeviceStatus.ONLINE, WindowsSessionState.PRIMARY_ACTIVE)));

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
                List.of(target("DEV-PC03", DeviceStatus.ONLINE, WindowsSessionState.NO_SESSION)));

        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.executableTargets())
                .extracting(ManagedAccountSwitchPreflightItem::action)
                .containsExactly(ManagedAccountSwitchAction.LOGON);
    }

    @Test
    void oppositeManagedAccountActiveRequiresSwitch() {
        var plan = planner.planSwitch(
                "OP-WIN-3",
                ManagedWindowsAccountType.PRIMARY,
                List.of(target("DEV-PC02", DeviceStatus.ONLINE, WindowsSessionState.SECONDARY_ACTIVE)));

        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.executableTargets())
                .extracting(ManagedAccountSwitchPreflightItem::action)
                .containsExactly(ManagedAccountSwitchAction.SWITCH);
    }

    @Test
    void plannerDoesNotUsePersistedDeviceStatusAsSessionAuthority() {
        var plan = planner.planSwitch(
                "OP-WIN-4",
                ManagedWindowsAccountType.PRIMARY,
                List.of(target("DEV-PC05", DeviceStatus.OFFLINE, WindowsSessionState.NO_SESSION)));

        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.pendingTargets()).isEmpty();
        assertThat(plan.executableTargets().getFirst().action()).isEqualTo(ManagedAccountSwitchAction.LOGON);
    }

    @Test
    void otherSessionActiveBlocksAsWindowsSessionChanged() {
        var plan = planner.planSwitch(
                "OP-WIN-5",
                ManagedWindowsAccountType.SECONDARY,
                List.of(target("DEV-PC04", DeviceStatus.ONLINE, WindowsSessionState.OTHER_SESSION_ACTIVE)));

        assertThat(plan.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(plan.blockedTargets().getFirst().errorCode()).isEqualTo(ErrorCode.WINDOWS_SESSION_CHANGED);
    }

    @Test
    void unknownSessionBlocksAsWindowsSessionUnknown() {
        var plan = planner.planSwitch(
                "OP-WIN-6",
                ManagedWindowsAccountType.PRIMARY,
                List.of(target("DEV-PC06", DeviceStatus.ONLINE, WindowsSessionState.UNKNOWN)));

        assertThat(plan.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(plan.blockedTargets().getFirst().errorCode()).isEqualTo(ErrorCode.WINDOWS_SESSION_UNKNOWN);
    }

    @Test
    void deferredClientReadinessStillAllowsRemoteSwitchPlanning() {
        var plan = planner.planSwitch(
                "OP-WIN-6B",
                ManagedWindowsAccountType.PRIMARY,
                List.of(target(
                        "DEV-PC06",
                        DeviceStatus.ONLINE,
                        WindowsSessionState.SECONDARY_ACTIVE,
                        ManagedWindowsAccount.notConfigured(ManagedWindowsAccountType.PRIMARY))));

        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.executableTargets().getFirst().action()).isEqualTo(ManagedAccountSwitchAction.SWITCH);
    }

    @Test
    void noSessionDoesNotRequireFabricatedCredentialReadinessInMaster() {
        var plan = planner.planSwitch(
                "OP-WIN-6C",
                ManagedWindowsAccountType.SECONDARY,
                List.of(target(
                        "DEV-PC06",
                        DeviceStatus.ONLINE,
                        WindowsSessionState.NO_SESSION,
                        ManagedWindowsAccount.credentialMissing(
                                ManagedWindowsAccountType.SECONDARY,
                                "GALTEK-STUDENT-SECONDARY"))));

        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.executableTargets().getFirst().action()).isEqualTo(ManagedAccountSwitchAction.LOGON);
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
        assertThat(plan.blockedTargets())
                .extracting(item -> item.target().targetId())
                .containsExactly("DEV-PC05");
        assertThat(plan.blockedTargets().getFirst().errorCode()).isEqualTo(ErrorCode.WINDOWS_SESSION_UNKNOWN);
    }

    @Test
    void targetSecondaryMatrixMatchesProductiveSnapshotSemantics() {
        var plan = planner.planSwitch(
                "OP-WIN-7B",
                ManagedWindowsAccountType.SECONDARY,
                List.of(
                        target("DEV-PC01", DeviceStatus.ONLINE, WindowsSessionState.SECONDARY_ACTIVE),
                        target("DEV-PC02", DeviceStatus.ONLINE, WindowsSessionState.PRIMARY_ACTIVE),
                        target("DEV-PC03", DeviceStatus.ONLINE, WindowsSessionState.NO_SESSION),
                        target("DEV-PC04", DeviceStatus.ONLINE, WindowsSessionState.OTHER_SESSION_ACTIVE),
                        target("DEV-PC05", DeviceStatus.ONLINE, WindowsSessionState.UNKNOWN)));

        assertThat(plan.noChangeTargets())
                .extracting(item -> item.target().targetId())
                .containsExactly("DEV-PC01");
        assertThat(plan.executableTargets())
                .extracting(ManagedAccountSwitchPreflightItem::action)
                .containsExactly(ManagedAccountSwitchAction.SWITCH, ManagedAccountSwitchAction.LOGON);
        assertThat(plan.blockedTargets())
                .extracting(ManagedAccountSwitchPreflightItem::errorCode)
                .containsExactly(ErrorCode.WINDOWS_SESSION_CHANGED, ErrorCode.WINDOWS_SESSION_UNKNOWN);
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
