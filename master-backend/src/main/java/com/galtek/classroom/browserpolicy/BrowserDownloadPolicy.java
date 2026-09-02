package com.galtek.classroom.browserpolicy;

import java.time.OffsetDateTime;

public record BrowserDownloadPolicy(
        String policyId,
        String classroomId,
        String name,
        BrowserDownloadRestrictionMode restrictionMode,
        BrowserPolicyScopeType scopeType,
        String schoolGroupId,
        String deviceId,
        BrowserPolicyAccountScope accountScope,
        boolean active,
        long version,
        OffsetDateTime createdAtUtc,
        OffsetDateTime updatedAtUtc) {
}
