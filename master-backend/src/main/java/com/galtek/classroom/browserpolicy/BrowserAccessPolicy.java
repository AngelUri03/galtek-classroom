package com.galtek.classroom.browserpolicy;

import java.time.OffsetDateTime;

public record BrowserAccessPolicy(
        String policyId,
        String classroomId,
        String name,
        BrowserPolicyMode mode,
        BrowserPolicyScopeType scopeType,
        String schoolGroupId,
        String deviceId,
        BrowserPolicyAccountScope accountScope,
        boolean active,
        long version,
        OffsetDateTime createdAtUtc,
        OffsetDateTime updatedAtUtc) {
}
