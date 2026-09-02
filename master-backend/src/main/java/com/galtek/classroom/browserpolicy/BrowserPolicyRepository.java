package com.galtek.classroom.browserpolicy;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface BrowserPolicyRepository {

    boolean classroomExists(String classroomId);

    boolean groupBelongsToClassroom(String groupId, String classroomId);

    boolean deviceBelongsToClassroom(String deviceId, String classroomId);

    List<BrowserAccessPolicy> findPoliciesByClassroomId(String classroomId, Boolean active);

    Optional<BrowserAccessPolicy> findPolicyById(String policyId);

    void createPolicy(BrowserAccessPolicy policy);

    void updatePolicy(
            String policyId,
            String name,
            BrowserPolicyMode mode,
            BrowserPolicyScopeType scopeType,
            String schoolGroupId,
            String deviceId,
            BrowserPolicyAccountScope accountScope,
            long expectedVersion,
            OffsetDateTime updatedAtUtc);

    void archivePolicy(String policyId, long expectedVersion, OffsetDateTime updatedAtUtc);

    List<BrowserUrlRule> findRulesByPolicyId(String policyId, Boolean enabled);

    Optional<BrowserUrlRule> findRuleById(String ruleId);

    void createRule(BrowserUrlRule rule);

    void updateRule(
            String ruleId,
            BrowserUrlRuleAction action,
            BrowserUrlMatchType matchType,
            String pattern,
            boolean enabled,
            String description,
            long expectedVersion,
            OffsetDateTime updatedAtUtc);

    void archiveRule(String ruleId, long expectedVersion, OffsetDateTime updatedAtUtc);
}
