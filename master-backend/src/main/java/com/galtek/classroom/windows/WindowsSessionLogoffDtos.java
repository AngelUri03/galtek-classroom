package com.galtek.classroom.windows;

import java.util.List;

public final class WindowsSessionLogoffDtos {
    private WindowsSessionLogoffDtos() {
    }

    public record WindowsSessionLogoffBatchResponse(
            String operationId,
            String type,
            String accountId,
            String status,
            int targetCount,
            List<WindowsSessionLogoffTargetResponse> targets) {
    }

    public record WindowsSessionLogoffTargetResponse(
            String deviceId,
            String status,
            String errorCode,
            String message) {
    }
}
