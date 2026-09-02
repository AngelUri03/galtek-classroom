package com.galtek.classroom.browserpolicy;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface BrowserDownloadPolicyRepository {

    boolean classroomExists(String classroomId);

    boolean groupBelongsToClassroom(String groupId, String classroomId);

    boolean deviceBelongsToClassroom(String deviceId, String classroomId);

    List<BrowserDownloadPolicy> findPoliciesByClassroomId(String classroomId, Boolean active);

    Optional<BrowserDownloadPolicy> findPolicyById(String policyId);

    void createPolicy(BrowserDownloadPolicy policy);

    void updatePolicy(
            String policyId,
            String name,
            BrowserDownloadRestrictionMode restrictionMode,
            BrowserPolicyScopeType scopeType,
            String schoolGroupId,
            String deviceId,
            BrowserPolicyAccountScope accountScope,
            long expectedVersion,
            OffsetDateTime updatedAtUtc);

    void archivePolicy(String policyId, long expectedVersion, OffsetDateTime updatedAtUtc);
}
