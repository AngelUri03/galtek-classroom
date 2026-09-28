package com.galtek.classroom.windows;

import com.galtek.classroom.operations.ErrorCode;
import java.util.List;

public final class WindowsSessionStateDtos {

    private WindowsSessionStateDtos() {
    }

    public record WindowsSessionStateBatchResponse(
            String classroomId,
            int targetCount,
            List<WindowsSessionStateTargetResponse> targets) {
    }

    public record WindowsSessionStateTargetResponse(
            String deviceId,
            String state,
            boolean online,
            String errorCode,
            String message) {

        static WindowsSessionStateTargetResponse success(
                String deviceId,
                WindowsSessionState state) {
            return new WindowsSessionStateTargetResponse(
                    deviceId,
                    state.name(),
                    true,
                    null,
                    "Windows session state observed.");
        }

        static WindowsSessionStateTargetResponse failed(
                String deviceId,
                ErrorCode errorCode,
                String message,
                boolean online) {
            return new WindowsSessionStateTargetResponse(
                    deviceId,
                    WindowsSessionState.UNKNOWN.name(),
                    online,
                    errorCode.name(),
                    message);
        }
    }
}
