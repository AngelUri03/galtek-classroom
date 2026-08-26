package com.galtek.classroom.localagent;

public record MasterAuthorizationResponse(
        String status,
        boolean authorized,
        boolean configured,
        String boundAccountDisplayName,
        String currentAccountDisplayName) {
}
