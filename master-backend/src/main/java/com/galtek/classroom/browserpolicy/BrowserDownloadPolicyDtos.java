package com.galtek.classroom.browserpolicy;

import com.fasterxml.jackson.annotation.JsonAnySetter;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import java.time.OffsetDateTime;

public final class BrowserDownloadPolicyDtos {

    private BrowserDownloadPolicyDtos() {
    }

    public record BrowserDownloadPolicyResponse(
            String policyId,
            String classroomId,
            String name,
            String restrictionMode,
            String scopeType,
            String schoolGroupId,
            String deviceId,
            String accountScope,
            boolean active,
            long version,
            OffsetDateTime createdAtUtc,
            OffsetDateTime updatedAtUtc) {

        public static BrowserDownloadPolicyResponse from(BrowserDownloadPolicy policy) {
            return new BrowserDownloadPolicyResponse(
                    policy.policyId(),
                    policy.classroomId(),
                    policy.name(),
                    policy.restrictionMode().name(),
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

    @JsonIgnoreProperties(ignoreUnknown = false)
    public record CreateBrowserDownloadPolicyRequest(
            String name,
            String restrictionMode,
            String scopeType,
            String schoolGroupId,
            String deviceId,
            String accountScope) {

        @JsonAnySetter
        public void unsupported(String name, Object value) {
            throw new IllegalArgumentException("Unsupported browser download policy field: " + name);
        }
    }

    @JsonIgnoreProperties(ignoreUnknown = false)
    public record UpdateBrowserDownloadPolicyRequest(
            String name,
            String restrictionMode,
            String scopeType,
            String schoolGroupId,
            String deviceId,
            String accountScope,
            Long expectedVersion) {

        @JsonAnySetter
        public void unsupported(String name, Object value) {
            throw new IllegalArgumentException("Unsupported browser download policy field: " + name);
        }
    }

    @JsonIgnoreProperties(ignoreUnknown = false)
    public record ArchiveBrowserDownloadPolicyRequest(Long expectedVersion) {

        @JsonAnySetter
        public void unsupported(String name, Object value) {
            throw new IllegalArgumentException("Unsupported browser download policy field: " + name);
        }
    }

    public record EffectiveBrowserDownloadPolicyResponse(
            BrowserDownloadPolicyResponse policy,
            String restrictionMode,
            boolean implicit) {

        public static EffectiveBrowserDownloadPolicyResponse from(BrowserDownloadPolicyResolution resolution) {
            return new EffectiveBrowserDownloadPolicyResponse(
                    resolution.policy() == null ? null : BrowserDownloadPolicyResponse.from(resolution.policy()),
                    resolution.restrictionMode().name(),
                    resolution.implicit());
        }
    }
}
