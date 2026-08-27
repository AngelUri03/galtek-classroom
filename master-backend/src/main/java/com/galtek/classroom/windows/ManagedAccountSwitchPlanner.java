package com.galtek.classroom.windows;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.device.Device;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationTarget;
import com.galtek.classroom.operations.OperationTargetType;
import com.galtek.classroom.operations.PreflightStatus;
import java.util.List;

public final class ManagedAccountSwitchPlanner {

    public ManagedAccountSwitchPlan planSwitch(
            String operationId,
            ManagedWindowsAccountType targetAccountType,
            List<ManagedAccountSwitchTarget> targets) {
        operationId = requireNonBlank(operationId, "operationId");
        requireNonNull(targetAccountType, "targetAccountType");
        targets = copyList(targets, "targets");

        return ManagedAccountSwitchPlan.fromTargets(
                operationId,
                targetAccountType,
                targets.stream()
                        .map(target -> planTarget(target, targetAccountType))
                        .toList());
    }

    private ManagedAccountSwitchPreflightItem planTarget(
            ManagedAccountSwitchTarget target,
            ManagedWindowsAccountType targetAccountType) {
        Device device = target.device();
        OperationTarget operationTarget = new OperationTarget(
                OperationTargetType.DEVICE,
                device.deviceId(),
                device.displayName());

        if (!device.availableForInteractiveOperation()) {
            return new ManagedAccountSwitchPreflightItem(
                    operationTarget,
                    targetAccountType,
                    target.sessionState(),
                    ManagedAccountSwitchAction.PENDING,
                    PreflightStatus.BLOCKED,
                    device.status().toOperationalError(),
                    "Device is not available for managed account switching.");
        }

        ManagedWindowsAccount targetAccount = target.account(targetAccountType);
        if (targetAccount == null || !targetAccount.configured()
                || targetAccount.status() == ManagedWindowsAccountStatus.NOT_CONFIGURED) {
            return blocked(
                    operationTarget,
                    targetAccountType,
                    target.sessionState(),
                    ErrorCode.ACCOUNT_NOT_CONFIGURED,
                    "Managed account " + targetAccountType.name() + " is not configured.");
        }

        if (!targetAccount.credentialConfigured()
                || targetAccount.status() == ManagedWindowsAccountStatus.CREDENTIAL_NOT_CONFIGURED) {
            return blocked(
                    operationTarget,
                    targetAccountType,
                    target.sessionState(),
                    ErrorCode.MANAGED_CREDENTIAL_NOT_CONFIGURED,
                    "Managed account credential is not configured.");
        }

        if (target.sessionState().matches(targetAccountType)) {
            return ready(
                    operationTarget,
                    targetAccountType,
                    target.sessionState(),
                    ManagedAccountSwitchAction.NO_CHANGE,
                    "Target managed account is already active.");
        }

        if (target.sessionState() == WindowsSessionState.NO_SESSION) {
            return ready(
                    operationTarget,
                    targetAccountType,
                    target.sessionState(),
                    ManagedAccountSwitchAction.LOGON,
                    "Managed account logon is required.");
        }

        if (target.sessionState().managedAccountActive()) {
            return ready(
                    operationTarget,
                    targetAccountType,
                    target.sessionState(),
                    ManagedAccountSwitchAction.SWITCH,
                    "Managed account switch is required.");
        }

        return blocked(
                operationTarget,
                targetAccountType,
                target.sessionState(),
                ErrorCode.WINDOWS_SESSION_UNKNOWN,
                "Windows session state does not allow an automatic managed account switch.");
    }

    private ManagedAccountSwitchPreflightItem ready(
            OperationTarget operationTarget,
            ManagedWindowsAccountType targetAccountType,
            WindowsSessionState sessionState,
            ManagedAccountSwitchAction action,
            String message) {
        return new ManagedAccountSwitchPreflightItem(
                operationTarget,
                targetAccountType,
                sessionState,
                action,
                PreflightStatus.READY,
                null,
                message);
    }

    private ManagedAccountSwitchPreflightItem blocked(
            OperationTarget operationTarget,
            ManagedWindowsAccountType targetAccountType,
            WindowsSessionState sessionState,
            ErrorCode errorCode,
            String message) {
        return new ManagedAccountSwitchPreflightItem(
                operationTarget,
                targetAccountType,
                sessionState,
                ManagedAccountSwitchAction.BLOCKED,
                PreflightStatus.BLOCKED,
                errorCode,
                message);
    }
}
