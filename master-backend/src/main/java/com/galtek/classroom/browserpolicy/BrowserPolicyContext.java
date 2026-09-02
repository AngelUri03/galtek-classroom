package com.galtek.classroom.browserpolicy;

public record BrowserPolicyContext(
        String classroomId,
        String deviceId,
        String schoolGroupId,
        BrowserPolicyAccountScope accountScope) {
}
