package com.galtek.classroom.localagent;

public record MasterUnlockAuthorizationResponse(
        String status,
        boolean authorized,
        boolean configured) {
}
