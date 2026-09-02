package com.galtek.classroom.browserpolicy;

import java.time.OffsetDateTime;

public final class BrowserPolicyDtos {

    private BrowserPolicyDtos() {
    }

    public record BrowserPolicyResponse(
            String policyId,
            String classroomId,
            String name,
            String mode,
            String scopeType,
            String schoolGroupId,
            String deviceId,
            String accountScope,
            boolean active,
            long version,
            OffsetDateTime createdAtUtc,
            OffsetDateTime updatedAtUtc) {

        public static BrowserPolicyResponse from(BrowserAccessPolicy policy) {
            return new BrowserPolicyResponse(
                    policy.policyId(),
                    policy.classroomId(),
                    policy.name(),
                    policy.mode().name(),
                    policy.scopeType().name(),
                    policy.schoolGroupId(),
                    policy.deviceId(),
                    policy.accountScope().name(),
                    policy.active(),
                    policy.version(),
                    policy.createdAtUtc(),
                    policy.updatedAtUtc());
        }
    }

    public record CreateBrowserPolicyRequest(
            String name,
            String mode,
            String scopeType,
            String schoolGroupId,
            String deviceId,
            String accountScope) {
    }

    public record UpdateBrowserPolicyRequest(
            String name,
            String mode,
            String scopeType,
            String schoolGroupId,
            String deviceId,
            String accountScope,
            Long expectedVersion) {
    }

    public record ArchiveBrowserPolicyRequest(Long expectedVersion) {
    }

    public record BrowserUrlRuleResponse(
            String ruleId,
            String policyId,
            String action,
            String matchType,
            String pattern,
            boolean enabled,
            String description,
            long version,
            OffsetDateTime createdAtUtc,
            OffsetDateTime updatedAtUtc) {

        public static BrowserUrlRuleResponse from(BrowserUrlRule rule) {
            return new BrowserUrlRuleResponse(
                    rule.ruleId(),
                    rule.policyId(),
                    rule.action().name(),
                    rule.matchType().name(),
                    rule.pattern(),
                    rule.enabled(),
                    rule.description(),
                    rule.version(),
                    rule.createdAtUtc(),
                    rule.updatedAtUtc());
        }
    }

    public record CreateBrowserUrlRuleRequest(
            String action,
            String matchType,
            String pattern,
            Boolean enabled,
            String description) {
    }

    public record UpdateBrowserUrlRuleRequest(
            String action,
            String matchType,
            String pattern,
            Boolean enabled,
            String description,
            Long expectedVersion) {
    }

    public record ArchiveBrowserUrlRuleRequest(Long expectedVersion) {
    }

    public record EffectiveBrowserPolicyResponse(
            BrowserPolicyResponse policy,
            String mode,
            boolean implicit) {

        public static EffectiveBrowserPolicyResponse from(BrowserPolicyResolution resolution) {
            return new EffectiveBrowserPolicyResponse(
                    resolution.policy() == null ? null : BrowserPolicyResponse.from(resolution.policy()),
                    resolution.mode().name(),
                    resolution.implicit());
        }
    }
}
