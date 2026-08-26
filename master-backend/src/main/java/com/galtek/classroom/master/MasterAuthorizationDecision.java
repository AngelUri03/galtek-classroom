package com.galtek.classroom.master;

import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.operations.ErrorCode;

public record MasterAuthorizationDecision(
        MasterAuthorizationState state,
        ErrorCode errorCode,
        String currentWindowsSid,
        String authorizedWindowsSid) {

    public MasterAuthorizationDecision {
        requireNonNull(state, "state");
    }

    public boolean authorized() {
        return state == MasterAuthorizationState.AUTHORIZED;
    }
}
