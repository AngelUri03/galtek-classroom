package com.galtek.classroom.browserpolicy;

import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageException;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.time.Clock;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class BrowserPolicyAdminService {

    private static final int MAX_NAME_LENGTH = 120;
    private static final int MAX_DESCRIPTION_LENGTH = 500;

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final BrowserPolicyRepository repository;
    private final BrowserUrlNormalizer normalizer;
    private final BrowserPolicyPrecedenceResolver resolver;
    private final Clock clock;

    public BrowserPolicyAdminService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            BrowserPolicyRepository repository,
            Clock clock) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.repository = repository;
        this.normalizer = new BrowserUrlNormalizer();
        this.resolver = new BrowserPolicyPrecedenceResolver();
        this.clock = clock;
    }

    public List<BrowserPolicyDtos.BrowserPolicyResponse> policies(String classroomId, Boolean active) {
        requireAuthorizedAndStorage();
        String cleanClassroomId = classroomOr404(classroomId);
        return repository.findPoliciesByClassroomId(cleanClassroomId, active == null ? true : active)
                .stream()
                .map(BrowserPolicyDtos.BrowserPolicyResponse::from)
                .toList();
    }

    @Transactional
    public BrowserPolicyDtos.BrowserPolicyResponse createPolicy(
            String classroomId,
            BrowserPolicyDtos.CreateBrowserPolicyRequest request) {
        requireAuthorizedAndStorage();
        String cleanClassroomId = classroomOr404(classroomId);
        PolicyFields fields = policyFields(
                cleanClassroomId,
                request == null ? null : request.name(),
                request == null ? null : request.mode(),
                request == null ? null : request.scopeType(),
                request == null ? null : request.schoolGroupId(),
                request == null ? null : request.deviceId(),
                request == null ? null : request.accountScope());
        OffsetDateTime nowUtc = nowUtc();
        String policyId = id();
        try {
            repository.createPolicy(new BrowserAccessPolicy(
                    policyId,
                    cleanClassroomId,
                    fields.name(),
                    fields.mode(),
                    fields.scopeType(),
                    fields.schoolGroupId(),
                    fields.deviceId(),
                    fields.accountScope(),
                    true,
                    0,
                    nowUtc,
                    nowUtc));
        } catch (MasterStorageException exception) {
            if (exception.errorCode() == ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION) {
                throw conflict(ErrorCode.BROWSER_POLICY_CONFLICT, "An active policy already exists for that scope.");
            }
            throw exception;
        }
        return BrowserPolicyDtos.BrowserPolicyResponse.from(policyOr404(policyId));
    }

    @Transactional
    public BrowserPolicyDtos.BrowserPolicyResponse updatePolicy(
            String policyId,
            BrowserPolicyDtos.UpdateBrowserPolicyRequest request) {
        requireAuthorizedAndStorage();
        BrowserAccessPolicy current = activePolicyOr404(policyId);
        String requestedScopeType = request == null ? null : request.scopeType();
        boolean scopeTypeChanges = requestedScopeType != null
                && enumValue(BrowserPolicyScopeType.class, requestedScopeType, "scopeType") != current.scopeType();
        PolicyFields fields = policyFields(
                current.classroomId(),
                request == null || request.name() == null ? current.name() : request.name(),
                request == null || request.mode() == null ? current.mode().name() : request.mode(),
                requestedScopeType == null ? current.scopeType().name() : requestedScopeType,
                request == null || request.schoolGroupId() == null
                        ? (scopeTypeChanges ? null : current.schoolGroupId())
                        : request.schoolGroupId(),
                request == null || request.deviceId() == null
                        ? (scopeTypeChanges ? null : current.deviceId())
                        : request.deviceId(),
                request == null || request.accountScope() == null
                        ? current.accountScope().name()
                        : request.accountScope());
        try {
            repository.updatePolicy(
                    current.policyId(),
                    fields.name(),
                    fields.mode(),
                    fields.scopeType(),
                    fields.schoolGroupId(),
                    fields.deviceId(),
                    fields.accountScope(),
                    expectedVersion(request == null ? null : request.expectedVersion()),
                    nowUtc());
        } catch (MasterStorageException exception) {
            if (exception.errorCode() == ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION) {
                throw conflict(ErrorCode.BROWSER_POLICY_CONFLICT, "An active policy already exists for that scope.");
            }
            throw exception;
        }
        return BrowserPolicyDtos.BrowserPolicyResponse.from(policyOr404(policyId));
    }

    @Transactional
    public BrowserPolicyDtos.BrowserPolicyResponse archivePolicy(
            String policyId,
            BrowserPolicyDtos.ArchiveBrowserPolicyRequest request) {
        requireAuthorizedAndStorage();
        BrowserAccessPolicy current = activePolicyOr404(policyId);
        repository.archivePolicy(
                current.policyId(),
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return BrowserPolicyDtos.BrowserPolicyResponse.from(policyOr404(policyId));
    }

    public List<BrowserPolicyDtos.BrowserUrlRuleResponse> rules(String policyId) {
        requireAuthorizedAndStorage();
        activePolicyOr404(policyId);
        return repository.findRulesByPolicyId(policyId, null)
                .stream()
                .map(BrowserPolicyDtos.BrowserUrlRuleResponse::from)
                .toList();
    }

    @Transactional
    public BrowserPolicyDtos.BrowserUrlRuleResponse createRule(
            String policyId,
            BrowserPolicyDtos.CreateBrowserUrlRuleRequest request) {
        requireAuthorizedAndStorage();
        activePolicyOr404(policyId);
        RuleFields fields = ruleFields(
                request == null ? null : request.action(),
                request == null ? null : request.matchType(),
                request == null ? null : request.pattern(),
                request == null || request.enabled() == null || request.enabled(),
                request == null ? null : request.description());
        OffsetDateTime nowUtc = nowUtc();
        String ruleId = id();
        repository.createRule(new BrowserUrlRule(
                ruleId,
                policyId,
                fields.action(),
                fields.matchType(),
                fields.pattern(),
                fields.enabled(),
                fields.description(),
                0,
                nowUtc,
                nowUtc));
        return BrowserPolicyDtos.BrowserUrlRuleResponse.from(ruleOr404(ruleId));
    }

    @Transactional
    public BrowserPolicyDtos.BrowserUrlRuleResponse updateRule(
            String ruleId,
            BrowserPolicyDtos.UpdateBrowserUrlRuleRequest request) {
        requireAuthorizedAndStorage();
        BrowserUrlRule current = ruleOr404(ruleId);
        activePolicyOr404(current.policyId());
        RuleFields fields = ruleFields(
                request == null || request.action() == null ? current.action().name() : request.action(),
                request == null || request.matchType() == null ? current.matchType().name() : request.matchType(),
                request == null || request.pattern() == null ? current.pattern() : request.pattern(),
                request == null || request.enabled() == null ? current.enabled() : request.enabled(),
                request == null || request.description() == null ? current.description() : request.description());
        repository.updateRule(
                ruleId,
                fields.action(),
                fields.matchType(),
                fields.pattern(),
                fields.enabled(),
                fields.description(),
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return BrowserPolicyDtos.BrowserUrlRuleResponse.from(ruleOr404(ruleId));
    }

    @Transactional
    public BrowserPolicyDtos.BrowserUrlRuleResponse archiveRule(
            String ruleId,
            BrowserPolicyDtos.ArchiveBrowserUrlRuleRequest request) {
        requireAuthorizedAndStorage();
        BrowserUrlRule current = ruleOr404(ruleId);
        activePolicyOr404(current.policyId());
        repository.archiveRule(
                ruleId,
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return BrowserPolicyDtos.BrowserUrlRuleResponse.from(ruleOr404(ruleId));
    }

    public BrowserPolicyDtos.EffectiveBrowserPolicyResponse effectivePolicy(
            String classroomId,
            String deviceId,
            String groupId,
            String accountType) {
        requireAuthorizedAndStorage();
        String cleanClassroomId = classroomOr404(classroomId);
        String cleanDeviceId = optional(deviceId);
        String cleanGroupId = optional(groupId);
        if (cleanDeviceId != null && !repository.deviceBelongsToClassroom(cleanDeviceId, cleanClassroomId)) {
            throw validation(ErrorCode.BROWSER_POLICY_SCOPE_INVALID, "deviceId belongs to a different classroom.");
        }
        if (cleanGroupId != null && !repository.groupBelongsToClassroom(cleanGroupId, cleanClassroomId)) {
            throw validation(ErrorCode.BROWSER_POLICY_SCOPE_INVALID, "groupId belongs to a different classroom.");
        }

        BrowserPolicyAccountScope accountScope = optional(accountType) == null
                ? null
                : enumValue(BrowserPolicyAccountScope.class, accountType, "accountType");
        BrowserPolicyResolution resolution = resolver.resolve(
                new BrowserPolicyContext(cleanClassroomId, cleanDeviceId, cleanGroupId, accountScope),
                repository.findPoliciesByClassroomId(cleanClassroomId, true));
        return BrowserPolicyDtos.EffectiveBrowserPolicyResponse.from(resolution);
    }

    private PolicyFields policyFields(
            String classroomId,
            String name,
            String mode,
            String scopeType,
            String schoolGroupId,
            String deviceId,
            String accountScope) {
        BrowserPolicyScopeType parsedScopeType = enumValue(BrowserPolicyScopeType.class, scopeType, "scopeType");
        BrowserPolicyMode parsedMode = enumValue(BrowserPolicyMode.class, mode, "mode");
        BrowserPolicyAccountScope parsedAccountScope =
                enumValue(BrowserPolicyAccountScope.class, accountScope, "accountScope");
        String cleanName = required(name, "name");
        if (cleanName.length() > MAX_NAME_LENGTH) {
            throw validation(ErrorCode.BROWSER_POLICY_SCOPE_INVALID, "name is too long.");
        }

        String cleanGroupId = optional(schoolGroupId);
        String cleanDeviceId = optional(deviceId);
        switch (parsedScopeType) {
            case CLASSROOM -> {
                if (cleanGroupId != null || cleanDeviceId != null) {
                    throw validation(
                            ErrorCode.BROWSER_POLICY_SCOPE_INVALID,
                            "CLASSROOM scope cannot include schoolGroupId or deviceId.");
                }
            }
            case GROUP -> {
                if (cleanGroupId == null || cleanDeviceId != null) {
                    throw validation(
                            ErrorCode.BROWSER_POLICY_SCOPE_INVALID,
                            "GROUP scope requires schoolGroupId and no deviceId.");
                }
                if (!repository.groupBelongsToClassroom(cleanGroupId, classroomId)) {
                    throw validation(
                            ErrorCode.BROWSER_POLICY_SCOPE_INVALID,
                            "schoolGroupId belongs to a different classroom.");
                }
            }
            case DEVICE -> {
                if (cleanDeviceId == null || cleanGroupId != null) {
                    throw validation(
                            ErrorCode.BROWSER_POLICY_SCOPE_INVALID,
                            "DEVICE scope requires deviceId and no schoolGroupId.");
                }
                if (!repository.deviceBelongsToClassroom(cleanDeviceId, classroomId)) {
                    throw validation(
                            ErrorCode.BROWSER_POLICY_SCOPE_INVALID,
                            "deviceId belongs to a different classroom.");
                }
            }
        }

        return new PolicyFields(cleanName, parsedMode, parsedScopeType, cleanGroupId, cleanDeviceId, parsedAccountScope);
    }

    private RuleFields ruleFields(
            String action,
            String matchType,
            String pattern,
            boolean enabled,
            String description) {
        BrowserUrlRuleAction parsedAction = enumValue(BrowserUrlRuleAction.class, action, "action");
        BrowserUrlMatchType parsedMatchType = enumValue(BrowserUrlMatchType.class, matchType, "matchType");
        String canonicalPattern = normalizer.normalizeUrlRulePattern(parsedMatchType, pattern)
                .orElseThrow(() -> validation(
                        ErrorCode.BROWSER_POLICY_RULE_INVALID,
                        "pattern is invalid for matchType."));
        String cleanDescription = optional(description);
        if (cleanDescription != null && cleanDescription.length() > MAX_DESCRIPTION_LENGTH) {
            throw validation(ErrorCode.BROWSER_POLICY_RULE_INVALID, "description is too long.");
        }
        return new RuleFields(parsedAction, parsedMatchType, canonicalPattern, enabled, cleanDescription);
    }

    private MasterAuthorizationResponse requireAuthorizedAndStorage() {
        MasterAuthorizationResponse authorization = masterAccessGuard.requireAuthorized();
        MasterStorageHealth health = storageState.health();
        if (health.status() != MasterStorageStatus.READY) {
            String code = health.errorCode() == null
                    ? ErrorCode.MASTER_DATABASE_UNAVAILABLE.name()
                    : health.errorCode();
            throw new ApiException(
                    HttpStatus.SERVICE_UNAVAILABLE,
                    code,
                    "Master storage is unavailable.");
        }
        return authorization;
    }

    private String classroomOr404(String classroomId) {
        String cleanId = required(classroomId, "classroomId");
        if (!repository.classroomExists(cleanId)) {
            throw notFound(ErrorCode.CLASSROOM_NOT_FOUND, "Classroom was not found.");
        }
        return cleanId;
    }

    private BrowserAccessPolicy policyOr404(String policyId) {
        String cleanId = required(policyId, "policyId");
        return repository.findPolicyById(cleanId)
                .orElseThrow(() -> notFound(ErrorCode.BROWSER_POLICY_NOT_FOUND, "Browser policy was not found."));
    }

    private BrowserAccessPolicy activePolicyOr404(String policyId) {
        BrowserAccessPolicy policy = policyOr404(policyId);
        if (!policy.active()) {
            throw notFound(ErrorCode.BROWSER_POLICY_NOT_FOUND, "Browser policy was not found.");
        }
        return policy;
    }

    private BrowserUrlRule ruleOr404(String ruleId) {
        String cleanId = required(ruleId, "ruleId");
        return repository.findRuleById(cleanId)
                .orElseThrow(() -> notFound(ErrorCode.BROWSER_POLICY_NOT_FOUND, "Browser URL rule was not found."));
    }

    private <T extends Enum<T>> T enumValue(Class<T> type, String value, String fieldName) {
        String clean = required(value, fieldName);
        try {
            return Enum.valueOf(type, clean.toUpperCase(Locale.ROOT));
        } catch (IllegalArgumentException exception) {
            throw validation(ErrorCode.INVALID_REQUEST, fieldName + " is invalid.");
        }
    }

    private long expectedVersion(Long version) {
        if (version == null) {
            throw validation(ErrorCode.INVALID_REQUEST, "expectedVersion is required.");
        }
        if (version < 0) {
            throw validation(ErrorCode.INVALID_REQUEST, "expectedVersion cannot be negative.");
        }
        return version;
    }

    private String required(String value, String fieldName) {
        String clean = optional(value);
        if (clean == null) {
            throw validation(ErrorCode.INVALID_REQUEST, fieldName + " is required.");
        }
        return clean;
    }

    private String optional(String value) {
        if (value == null || value.isBlank()) {
            return null;
        }
        return value.trim();
    }

    private ApiException notFound(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.NOT_FOUND, errorCode, message);
    }

    private ApiException conflict(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.CONFLICT, errorCode, message);
    }

    private ApiException validation(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, errorCode, message);
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }

    private String id() {
        return UUID.randomUUID().toString();
    }

    private record PolicyFields(
            String name,
            BrowserPolicyMode mode,
            BrowserPolicyScopeType scopeType,
            String schoolGroupId,
            String deviceId,
            BrowserPolicyAccountScope accountScope) {
    }

    private record RuleFields(
            BrowserUrlRuleAction action,
            BrowserUrlMatchType matchType,
            String pattern,
            boolean enabled,
            String description) {
    }
}
