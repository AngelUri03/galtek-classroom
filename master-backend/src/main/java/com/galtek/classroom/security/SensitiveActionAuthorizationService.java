package com.galtek.classroom.security;

import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.activity.DeviceActivityRecorder;
import com.galtek.classroom.credentialvault.CredentialVaultException;
import com.galtek.classroom.credentialvault.CredentialVaultService;
import com.galtek.classroom.credentialvault.CredentialVaultService.FreshCredentialSnapshot;
import com.galtek.classroom.credentialvault.CredentialType;
import com.galtek.classroom.operations.ErrorCode;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Base64;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import org.springframework.http.HttpStatus;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;

@Service
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled", havingValue = "true", matchIfMissing = true)
public class SensitiveActionAuthorizationService {

    public static final String HEADER = "X-Galtek-Sensitive-Authorization";
    public static final Duration TTL = Duration.ofSeconds(60);
    private static final int TOKEN_BYTES = 32;
    private static final int MAX_FAILURES = 5;
    private static final Duration FAILURE_WINDOW = Duration.ofMinutes(2);
    private static final Duration COOLDOWN = Duration.ofSeconds(30);

    private final CredentialVaultService vaultService;
    private final Clock clock;
    private final SecureRandom random;
    private final DeviceActivityRecorder activityRecorder;
    private final Map<String, Authorization> authorizations = new HashMap<>();
    private final Map<String, FailureState> failures = new HashMap<>();

    @Autowired
    public SensitiveActionAuthorizationService(
            CredentialVaultService vaultService,
            Clock clock,
            DeviceActivityRecorder activityRecorder) {
        this(vaultService, clock, new SecureRandom(), activityRecorder);
    }

    SensitiveActionAuthorizationService(CredentialVaultService vaultService, Clock clock, SecureRandom random) {
        this(vaultService, clock, random, null);
    }

    SensitiveActionAuthorizationService(
            CredentialVaultService vaultService,
            Clock clock,
            SecureRandom random,
            DeviceActivityRecorder activityRecorder) {
        this.vaultService = vaultService;
        this.clock = clock;
        this.random = random;
        this.activityRecorder = activityRecorder;
    }

    public synchronized IssuedAuthorization authorize(
            String masterPassword,
            SensitiveActionScope scope,
            String actor,
            String classroomId,
            List<String> targetDeviceIds,
            String action) {
        Instant now = clock.instant();
        String cleanActor = required(actor, "actor");
        try {
            enforceRateLimit(cleanActor, now);
        } catch (ApiException exception) {
            recordAdminAuthorization(scope, classroomId, targetDeviceIds, cleanActor, "FAILED");
            throw exception;
        }
        List<FreshCredentialSnapshot> revealSnapshot;
        try {
            revealSnapshot = scope == SensitiveActionScope.CREDENTIAL_REVEAL
                    ? vaultService.freshCredentialSnapshot(masterPassword)
                    : List.of();
            if (scope != SensitiveActionScope.CREDENTIAL_REVEAL) vaultService.verifyMasterPassword(masterPassword);
        } catch (CredentialVaultException exception) {
            recordFailure(cleanActor, now);
            recordAdminAuthorization(scope, classroomId, targetDeviceIds, cleanActor, "FAILED");
            throw exception;
        }
        failures.remove(cleanActor);
        purgeExpired(now);

        String token = token();
        Instant expiresAt = now.plus(TTL);
        Authorization authorization = new Authorization(
                tokenDigest(token), scope, cleanActor, required(classroomId, "classroomId"),
                exactTargets(targetDeviceIds), required(action, "action"), expiresAt,
                scope == SensitiveActionScope.ADMIN_SESSION, false, revealSnapshot);
        authorizations.put(authorization.tokenDigest(), authorization);
        recordAdminAuthorization(scope, classroomId, targetDeviceIds, cleanActor, "SUCCESS");
        return new IssuedAuthorization(token, expiresAt);
    }

    public synchronized void consumeAdminSession(
            String token,
            String actor,
            String classroomId,
            List<String> targetDeviceIds) {
        Authorization authorization = requireMatching(
                token, SensitiveActionScope.ADMIN_SESSION, actor, classroomId,
                targetDeviceIds, "SWITCH_MANAGED_ACCOUNT_ADMIN");
        authorizations.put(authorization.tokenDigest(), authorization.usedCopy());
    }

    public synchronized void requireCredentialReveal(
            String token,
            String actor,
            String classroomId,
            String deviceId) {
        requireMatching(token, SensitiveActionScope.CREDENTIAL_REVEAL, actor, classroomId,
                List.of(required(deviceId, "deviceId")), "REVEAL_MANAGED_CREDENTIAL");
    }

    public synchronized RevealedCredential revealManagedCredential(
            String token,
            String actor,
            String classroomId,
            String deviceId,
            String expectedDisplayNamePrefix,
            String expectedLoginIdentifier) {
        Authorization authorization = requireMatching(token, SensitiveActionScope.CREDENTIAL_REVEAL,
                actor, classroomId, List.of(required(deviceId, "deviceId")), "REVEAL_MANAGED_CREDENTIAL");
        List<FreshCredentialSnapshot> matches = authorization.revealSnapshot().stream()
                .filter(entry -> entry.credentialType() == CredentialType.WINDOWS_ACCOUNT)
                .filter(entry -> entry.displayName().startsWith(required(expectedDisplayNamePrefix, "expectedDisplayNamePrefix")))
                .filter(entry -> localAccountName(entry.loginIdentifier())
                        .equalsIgnoreCase(localAccountName(required(expectedLoginIdentifier, "expectedLoginIdentifier"))))
                .toList();
        if (matches.isEmpty()) {
            throw new ApiException(HttpStatus.NOT_FOUND, ErrorCode.CREDENTIAL_NOT_FOUND,
                    "Managed credential is not present in the Master vault.");
        }
        if (matches.size() != 1) {
            throw new ApiException(HttpStatus.CONFLICT, ErrorCode.CREDENTIAL_NOT_PROVISIONABLE,
                    "Multiple vault credentials match this managed account.");
        }
        FreshCredentialSnapshot match = matches.getFirst();
        return new RevealedCredential(match.credentialId(), match.password());
    }

    public synchronized void auditDeliveredCredentialReveal(String credentialId) {
        vaultService.auditFreshReveal(required(credentialId, "credentialId"));
    }

    public synchronized void invalidateActor(String actor) {
        if (actor == null) return;
        authorizations.entrySet().removeIf(entry -> entry.getValue().actor().equals(actor));
    }

    private Authorization requireMatching(
            String token,
            SensitiveActionScope scope,
            String actor,
            String classroomId,
            List<String> targets,
            String action) {
        if (token == null || token.isBlank()) {
            throw forbidden(ErrorCode.SENSITIVE_AUTHORIZATION_REQUIRED,
                    "Fresh sensitive authorization is required.");
        }
        Instant now = clock.instant();
        Authorization authorization = authorizations.get(tokenDigest(token));
        if (authorization == null) {
            throw forbidden(ErrorCode.SENSITIVE_AUTHORIZATION_INVALID,
                    "Sensitive authorization is invalid.");
        }
        if (!now.isBefore(authorization.expiresAt())) {
            authorizations.remove(authorization.tokenDigest());
            throw forbidden(ErrorCode.SENSITIVE_AUTHORIZATION_EXPIRED,
                    "Sensitive authorization has expired.");
        }
        if (authorization.oneTime() && authorization.used()) {
            throw forbidden(ErrorCode.SENSITIVE_AUTHORIZATION_USED,
                    "Sensitive authorization has already been used.");
        }
        if (authorization.scope() != scope
                || !authorization.actor().equals(required(actor, "actor"))
                || !authorization.classroomId().equals(required(classroomId, "classroomId"))
                || !authorization.targetDeviceIds().equals(exactTargets(targets))
                || !authorization.action().equals(action)) {
            throw forbidden(ErrorCode.SENSITIVE_AUTHORIZATION_INVALID,
                    "Sensitive authorization scope does not match this action.");
        }
        return authorization;
    }

    private void enforceRateLimit(String actor, Instant now) {
        FailureState state = failures.get(actor);
        if (state == null) return;
        trimFailures(state.attempts(), now);
        if (state.cooldownUntil() != null && now.isBefore(state.cooldownUntil())) {
            throw new ApiException(HttpStatus.TOO_MANY_REQUESTS,
                    ErrorCode.SENSITIVE_AUTHORIZATION_RATE_LIMITED,
                    "Too many failed master password attempts. Try again shortly.");
        }
    }

    private void recordFailure(String actor, Instant now) {
        FailureState state = failures.computeIfAbsent(actor, ignored -> new FailureState(new ArrayDeque<>(), null));
        trimFailures(state.attempts(), now);
        state.attempts().addLast(now);
        if (state.attempts().size() >= MAX_FAILURES) {
            failures.put(actor, new FailureState(state.attempts(), now.plus(COOLDOWN)));
        }
    }

    private void trimFailures(ArrayDeque<Instant> attempts, Instant now) {
        Instant threshold = now.minus(FAILURE_WINDOW);
        while (!attempts.isEmpty() && attempts.getFirst().isBefore(threshold)) attempts.removeFirst();
    }

    private void purgeExpired(Instant now) {
        authorizations.entrySet().removeIf(entry -> !now.isBefore(entry.getValue().expiresAt()));
    }

    private void recordAdminAuthorization(
            SensitiveActionScope scope,
            String classroomId,
            List<String> targetDeviceIds,
            String actor,
            String result) {
        if (scope != SensitiveActionScope.ADMIN_SESSION || activityRecorder == null
                || targetDeviceIds == null) return;
        String eventType = "SUCCESS".equals(result)
                ? "ADMIN_SESSION_AUTHORIZED"
                : "ADMIN_SESSION_AUTHORIZATION_FAILED";
        for (String deviceId : targetDeviceIds) {
            if (deviceId != null && !deviceId.isBlank()) {
                activityRecorder.record(classroomId, deviceId, eventType, actor,
                        "ADMIN", null, result, null);
            }
        }
    }

    private List<String> exactTargets(List<String> targets) {
        if (targets == null || targets.isEmpty()) throw invalid("targetDeviceIds is required.");
        LinkedHashSet<String> unique = new LinkedHashSet<>();
        for (String target : targets) unique.add(required(target, "deviceId"));
        if (unique.size() != targets.size()) throw invalid("targetDeviceIds must not contain duplicates.");
        ArrayList<String> sorted = new ArrayList<>(unique);
        sorted.sort(String::compareTo);
        return List.copyOf(sorted);
    }

    private String token() {
        byte[] bytes = new byte[TOKEN_BYTES];
        random.nextBytes(bytes);
        try {
            return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
        } finally {
            java.util.Arrays.fill(bytes, (byte) 0);
        }
    }

    private String tokenDigest(String token) {
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256")
                    .digest(token.getBytes(java.nio.charset.StandardCharsets.US_ASCII));
            return Base64.getEncoder().encodeToString(digest);
        } catch (java.security.GeneralSecurityException exception) {
            throw new IllegalStateException("SHA-256 is unavailable.", exception);
        }
    }

    private String required(String value, String field) {
        if (value == null || value.isBlank()) throw invalid(field + " is required.");
        return value.trim();
    }

    private String localAccountName(String loginIdentifier) {
        String clean = required(loginIdentifier, "loginIdentifier");
        int separator = clean.lastIndexOf('\\');
        return separator >= 0 ? clean.substring(separator + 1) : clean;
    }

    private ApiException invalid(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, message);
    }

    private ApiException forbidden(ErrorCode code, String message) {
        return new ApiException(HttpStatus.FORBIDDEN, code, message);
    }

    public record IssuedAuthorization(String token, Instant expiresAtUtc) {}

    public record RevealedCredential(String credentialId, String password) {}

    private record FailureState(ArrayDeque<Instant> attempts, Instant cooldownUntil) {}

    private record Authorization(
            String tokenDigest,
            SensitiveActionScope scope,
            String actor,
            String classroomId,
            List<String> targetDeviceIds,
            String action,
            Instant expiresAt,
            boolean oneTime,
            boolean used,
            List<FreshCredentialSnapshot> revealSnapshot) {
        Authorization usedCopy() {
            return new Authorization(tokenDigest, scope, actor, classroomId, targetDeviceIds,
                    action, expiresAt, oneTime, true, revealSnapshot);
        }
    }
}
