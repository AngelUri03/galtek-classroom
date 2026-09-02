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
public class BrowserDownloadPolicyAdminService {

    private static final int MAX_NAME_LENGTH = 120;

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final BrowserDownloadPolicyRepository repository;
    private final BrowserDownloadPolicyPrecedenceResolver resolver;
    private final Clock clock;

    public BrowserDownloadPolicyAdminService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            BrowserDownloadPolicyRepository repository,
            Clock clock) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.repository = repository;
        this.resolver = new BrowserDownloadPolicyPrecedenceResolver();
        this.clock = clock;
    }

    public List<BrowserDownloadPolicyDtos.BrowserDownloadPolicyResponse> policies(
            String classroomId,
            Boolean active) {
        requireAuthorizedAndStorage();
        String cleanClassroomId = classroomOr404(classroomId);
        return repository.findPoliciesByClassroomId(cleanClassroomId, active == null ? true : active)
                .stream()
                .map(BrowserDownloadPolicyDtos.BrowserDownloadPolicyResponse::from)
                .toList();
    }

    @Transactional
    public BrowserDownloadPolicyDtos.BrowserDownloadPolicyResponse createPolicy(
            String classroomId,
            BrowserDownloadPolicyDtos.CreateBrowserDownloadPolicyRequest request) {
        requireAuthorizedAndStorage();
        String cleanClassroomId = classroomOr404(classroomId);
        PolicyFields fields = policyFields(
                cleanClassroomId,
                request == null ? null : request.name(),
                request == null ? null : request.restrictionMode(),
                request == null ? null : request.scopeType(),
                request == null ? null : request.schoolGroupId(),
                request == null ? null : request.deviceId(),
                request == null ? null : request.accountScope());
        OffsetDateTime nowUtc = nowUtc();
        String policyId = id();
        try {
            repository.createPolicy(new BrowserDownloadPolicy(
                    policyId,
                    cleanClassroomId,
                    fields.name(),
                    fields.restrictionMode(),
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
                throw conflict(
                        ErrorCode.BROWSER_DOWNLOAD_POLICY_CONFLICT,
                        "An active browser download policy already exists for that scope.");
            }
            throw exception;
        }
        return BrowserDownloadPolicyDtos.BrowserDownloadPolicyResponse.from(policyOr404(policyId));
    }

    @Transactional
    public BrowserDownloadPolicyDtos.BrowserDownloadPolicyResponse updatePolicy(
            String policyId,
            BrowserDownloadPolicyDtos.UpdateBrowserDownloadPolicyRequest request) {
        requireAuthorizedAndStorage();
        BrowserDownloadPolicy current = activePolicyOr404(policyId);
        String requestedScopeType = request == null ? null : request.scopeType();
        boolean scopeTypeChanges = requestedScopeType != null
                && enumValue(BrowserPolicyScopeType.class, requestedScopeType, "scopeType") != current.scopeType();
        PolicyFields fields = policyFields(
                current.classroomId(),
                request == null || request.name() == null ? current.name() : request.name(),
                request == null || request.restrictionMode() == null
                        ? current.restrictionMode().name()
                        : request.restrictionMode(),
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
                    fields.restrictionMode(),
                    fields.scopeType(),
                    fields.schoolGroupId(),
                    fields.deviceId(),
                    fields.accountScope(),
                    expectedVersion(request == null ? null : request.expectedVersion()),
                    nowUtc());
        } catch (MasterStorageException exception) {
            if (exception.errorCode() == ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION) {
                throw conflict(
                        ErrorCode.BROWSER_DOWNLOAD_POLICY_CONFLICT,
                        "An active browser download policy already exists for that scope.");
            }
            throw exception;
        }
        return BrowserDownloadPolicyDtos.BrowserDownloadPolicyResponse.from(policyOr404(policyId));
    }

    @Transactional
    public BrowserDownloadPolicyDtos.BrowserDownloadPolicyResponse archivePolicy(
            String policyId,
            BrowserDownloadPolicyDtos.ArchiveBrowserDownloadPolicyRequest request) {
        requireAuthorizedAndStorage();
        BrowserDownloadPolicy current = activePolicyOr404(policyId);
        repository.archivePolicy(
                current.policyId(),
                expectedVersion(request == null ? null : request.expectedVersion()),
                nowUtc());
        return BrowserDownloadPolicyDtos.BrowserDownloadPolicyResponse.from(policyOr404(policyId));
    }

    public BrowserDownloadPolicyDtos.EffectiveBrowserDownloadPolicyResponse effectivePolicy(
            String classroomId,
            String deviceId,
            String groupId,
            String accountType) {
        requireAuthorizedAndStorage();
        String cleanClassroomId = classroomOr404(classroomId);
        String cleanDeviceId = optional(deviceId);
        String cleanGroupId = optional(groupId);
        if (cleanDeviceId != null && !repository.deviceBelongsToClassroom(cleanDeviceId, cleanClassroomId)) {
            throw validation(
                    ErrorCode.BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID,
                    "deviceId belongs to a different classroom.");
        }
        if (cleanGroupId != null && !repository.groupBelongsToClassroom(cleanGroupId, cleanClassroomId)) {
            throw validation(
                    ErrorCode.BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID,
                    "groupId belongs to a different classroom.");
        }

        BrowserPolicyAccountScope accountScope = optional(accountType) == null
                ? null
                : enumValue(BrowserPolicyAccountScope.class, accountType, "accountType");
        BrowserDownloadPolicyResolution resolution = resolver.resolve(
                new BrowserDownloadPolicyContext(cleanClassroomId, cleanDeviceId, cleanGroupId, accountScope),
                repository.findPoliciesByClassroomId(cleanClassroomId, true));
        return BrowserDownloadPolicyDtos.EffectiveBrowserDownloadPolicyResponse.from(resolution);
    }

    private PolicyFields policyFields(
            String classroomId,
            String name,
            String restrictionMode,
            String scopeType,
            String schoolGroupId,
            String deviceId,
            String accountScope) {
        BrowserPolicyScopeType parsedScopeType = enumValue(BrowserPolicyScopeType.class, scopeType, "scopeType");
        BrowserDownloadRestrictionMode parsedRestrictionMode =
                enumValue(BrowserDownloadRestrictionMode.class, restrictionMode, "restrictionMode");
        BrowserPolicyAccountScope parsedAccountScope =
                enumValue(BrowserPolicyAccountScope.class, accountScope, "accountScope");
        String cleanName = required(name, "name");
        if (cleanName.length() > MAX_NAME_LENGTH) {
            throw validation(ErrorCode.BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID, "name is too long.");
        }

        String cleanGroupId = optional(schoolGroupId);
        String cleanDeviceId = optional(deviceId);
        switch (parsedScopeType) {
            case CLASSROOM -> {
                if (cleanGroupId != null || cleanDeviceId != null) {
                    throw validation(
                            ErrorCode.BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID,
                            "CLASSROOM scope cannot include schoolGroupId or deviceId.");
                }
            }
            case GROUP -> {
                if (cleanGroupId == null || cleanDeviceId != null) {
                    throw validation(
                            ErrorCode.BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID,
                            "GROUP scope requires schoolGroupId and no deviceId.");
                }
                if (!repository.groupBelongsToClassroom(cleanGroupId, classroomId)) {
                    throw validation(
                            ErrorCode.BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID,
                            "schoolGroupId belongs to a different classroom.");
                }
            }
            case DEVICE -> {
                if (cleanDeviceId == null || cleanGroupId != null) {
                    throw validation(
                            ErrorCode.BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID,
                            "DEVICE scope requires deviceId and no schoolGroupId.");
                }
                if (!repository.deviceBelongsToClassroom(cleanDeviceId, classroomId)) {
                    throw validation(
                            ErrorCode.BROWSER_DOWNLOAD_POLICY_SCOPE_INVALID,
                            "deviceId belongs to a different classroom.");
                }
            }
        }

        return new PolicyFields(
                cleanName,
                parsedRestrictionMode,
                parsedScopeType,
                cleanGroupId,
                cleanDeviceId,
                parsedAccountScope);
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

    private BrowserDownloadPolicy policyOr404(String policyId) {
        String cleanId = required(policyId, "policyId");
        return repository.findPolicyById(cleanId)
                .orElseThrow(() -> notFound(
                        ErrorCode.BROWSER_DOWNLOAD_POLICY_NOT_FOUND,
                        "Browser download policy was not found."));
    }

    private BrowserDownloadPolicy activePolicyOr404(String policyId) {
        BrowserDownloadPolicy policy = policyOr404(policyId);
        if (!policy.active()) {
            throw notFound(ErrorCode.BROWSER_DOWNLOAD_POLICY_NOT_FOUND, "Browser download policy was not found.");
        }
        return policy;
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
            BrowserDownloadRestrictionMode restrictionMode,
            BrowserPolicyScopeType scopeType,
            String schoolGroupId,
            String deviceId,
            BrowserPolicyAccountScope accountScope) {
    }
}
