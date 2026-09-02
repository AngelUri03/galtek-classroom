package com.galtek.classroom.browserpolicy;

import java.time.OffsetDateTime;

public record BrowserUrlRule(
        String ruleId,
        String policyId,
        BrowserUrlRuleAction action,
        BrowserUrlMatchType matchType,
        String pattern,
        boolean enabled,
        String description,
        long version,
        OffsetDateTime createdAtUtc,
        OffsetDateTime updatedAtUtc) {
}
