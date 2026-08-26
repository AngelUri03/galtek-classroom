package com.galtek.classroom.operations;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.util.List;

public record BatchPreflightPlan(
        String operationId,
        OperationType operationType,
        PreflightStatus status,
        List<BatchPreflightItem> targets) {

    public BatchPreflightPlan {
        operationId = requireNonBlank(operationId, "operationId");
        requireNonNull(operationType, "operationType");
        requireNonNull(status, "status");
        targets = copyList(targets, "targets");
    }

    public static BatchPreflightPlan fromTargets(
            String operationId,
            OperationType operationType,
            List<BatchPreflightItem> targets) {
        var copiedTargets = List.copyOf(targets);

        return new BatchPreflightPlan(operationId, operationType, deriveStatus(copiedTargets), copiedTargets);
    }

    public List<BatchPreflightItem> readyTargets() {
        return targets.stream()
                .filter(target -> target.status() == PreflightStatus.READY)
                .toList();
    }

    public List<BatchPreflightItem> warningTargets() {
        return targets.stream()
                .filter(target -> target.status() == PreflightStatus.WARNING)
                .toList();
    }

    public List<BatchPreflightItem> blockedTargets() {
        return targets.stream()
                .filter(target -> target.status() == PreflightStatus.BLOCKED)
                .toList();
    }

    private static PreflightStatus deriveStatus(List<BatchPreflightItem> targets) {
        if (targets.isEmpty() || targets.stream().allMatch(target -> target.status() == PreflightStatus.READY)) {
            return PreflightStatus.READY;
        }

        if (targets.stream().allMatch(target -> target.status() == PreflightStatus.BLOCKED)) {
            return PreflightStatus.BLOCKED;
        }

        return PreflightStatus.WARNING;
    }
}
