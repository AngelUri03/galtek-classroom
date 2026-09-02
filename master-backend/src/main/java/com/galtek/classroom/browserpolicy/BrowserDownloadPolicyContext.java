package com.galtek.classroom.browserpolicy;

public record BrowserDownloadPolicyContext(
        String classroomId,
        String deviceId,
        String schoolGroupId,
        BrowserPolicyAccountScope accountScope) {
}
