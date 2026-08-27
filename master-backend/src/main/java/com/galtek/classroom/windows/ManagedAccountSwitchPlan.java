package com.galtek.classroom.windows;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.PreflightStatus;
import java.util.List;

public record ManagedAccountSwitchPlan(
        String operationId,
        OperationType operationType,
        ManagedWindowsAccountType targetAccountType,
        PreflightStatus status,
        List<ManagedAccountSwitchPreflightItem> targets) {

    public ManagedAccountSwitchPlan {
        operationId = requireNonBlank(operationId, "operationId");
        requireNonNull(operationType, "operationType");
        if (operationType != OperationType.SWITCH_MANAGED_ACCOUNT) {
            throw new IllegalArgumentException("operationType must be SWITCH_MANAGED_ACCOUNT.");
        }
        requireNonNull(targetAccountType, "targetAccountType");
        requireNonNull(status, "status");
        targets = copyList(targets, "targets");
    }

    public static ManagedAccountSwitchPlan fromTargets(
            String operationId,
            ManagedWindowsAccountType targetAccountType,
            List<ManagedAccountSwitchPreflightItem> targets) {
        var copiedTargets = List.copyOf(targets);

        return new ManagedAccountSwitchPlan(
                operationId,
                OperationType.SWITCH_MANAGED_ACCOUNT,
                targetAccountType,
                deriveStatus(copiedTargets),
                copiedTargets);
    }

    public List<ManagedAccountSwitchPreflightItem> noChangeTargets() {
        return targets.stream()
                .filter(target -> target.action() == ManagedAccountSwitchAction.NO_CHANGE)
                .toList();
    }

    public List<ManagedAccountSwitchPreflightItem> executableTargets() {
        return targets.stream()
                .filter(target -> target.status() == PreflightStatus.READY)
                .filter(target -> target.action() == ManagedAccountSwitchAction.LOGON
                        || target.action() == ManagedAccountSwitchAction.SWITCH)
                .toList();
    }

    public List<ManagedAccountSwitchPreflightItem> blockedTargets() {
        return targets.stream()
                .filter(target -> target.status() == PreflightStatus.BLOCKED)
                .toList();
    }

    public List<ManagedAccountSwitchPreflightItem> pendingTargets() {
        return targets.stream()
                .filter(target -> target.action() == ManagedAccountSwitchAction.PENDING)
                .toList();
    }

    private static PreflightStatus deriveStatus(List<ManagedAccountSwitchPreflightItem> targets) {
        if (targets.isEmpty() || targets.stream().allMatch(target -> target.status() == PreflightStatus.READY)) {
            return PreflightStatus.READY;
        }

        if (targets.stream().allMatch(target -> target.status() == PreflightStatus.BLOCKED)) {
            return PreflightStatus.BLOCKED;
        }

        return PreflightStatus.WARNING;
    }
}
