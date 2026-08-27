package com.galtek.classroom.windows;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationTarget;
import com.galtek.classroom.operations.PreflightStatus;

public record ManagedAccountSwitchPreflightItem(
        OperationTarget target,
        ManagedWindowsAccountType targetAccountType,
        WindowsSessionState sessionState,
        ManagedAccountSwitchAction action,
        PreflightStatus status,
        ErrorCode errorCode,
        String message) {

    public ManagedAccountSwitchPreflightItem {
        requireNonNull(target, "target");
        requireNonNull(targetAccountType, "targetAccountType");
        requireNonNull(sessionState, "sessionState");
        requireNonNull(action, "action");
        requireNonNull(status, "status");
        if (status == PreflightStatus.BLOCKED && errorCode == null) {
            throw new IllegalArgumentException("errorCode is required for blocked switch targets.");
        }
        if (status != PreflightStatus.BLOCKED && action == ManagedAccountSwitchAction.BLOCKED) {
            throw new IllegalArgumentException("BLOCKED action requires blocked preflight status.");
        }
    }
}
