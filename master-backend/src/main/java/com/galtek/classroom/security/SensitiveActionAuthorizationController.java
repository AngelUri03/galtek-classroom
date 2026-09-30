package com.galtek.classroom.security;

import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.credentialvault.CredentialVaultController;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.operations.ErrorCode;
import java.util.Arrays;
import java.util.List;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;

@RestController
@RequestMapping("/api")
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled", havingValue = "true", matchIfMissing = true)
public class SensitiveActionAuthorizationController {

    private static final int MAX_MASTER_PASSWORD_BYTES = 16 * 1024;
    private static final String TARGETS_HEADER = "X-Galtek-Target-Device-Ids";
    private final MasterAccessGuard accessGuard;
    private final SensitiveActionAuthorizationService service;

    public SensitiveActionAuthorizationController(
            MasterAccessGuard accessGuard,
            SensitiveActionAuthorizationService service) {
        this.accessGuard = accessGuard;
        this.service = service;
    }

    @PostMapping(path = "/classrooms/{classroomId}/sensitive-authorizations/admin-session",
            consumes = MediaType.APPLICATION_OCTET_STREAM_VALUE)
    public AuthorizationResponse authorizeAdminSession(
            @PathVariable String classroomId,
            @RequestHeader(TARGETS_HEADER) String rawTargetDeviceIds,
            @RequestBody byte[] body) {
        MasterAuthorizationResponse actor = accessGuard.requireAuthorized();
        return authorize(body, SensitiveActionScope.ADMIN_SESSION, actor, classroomId,
                parseTargets(rawTargetDeviceIds), "SWITCH_MANAGED_ACCOUNT_ADMIN");
    }

    @PostMapping(path = "/classrooms/{classroomId}/devices/{deviceId}/sensitive-authorizations/credential-reveal",
            consumes = MediaType.APPLICATION_OCTET_STREAM_VALUE)
    public AuthorizationResponse authorizeCredentialReveal(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @RequestBody byte[] body) {
        MasterAuthorizationResponse actor = accessGuard.requireAuthorized();
        return authorize(body, SensitiveActionScope.CREDENTIAL_REVEAL, actor, classroomId,
                List.of(deviceId), "REVEAL_MANAGED_CREDENTIAL");
    }

    private AuthorizationResponse authorize(
            byte[] body,
            SensitiveActionScope scope,
            MasterAuthorizationResponse actor,
            String classroomId,
            List<String> targets,
            String action) {
        String masterPassword = CredentialVaultController.decodeUtf8Secret(body, MAX_MASTER_PASSWORD_BYTES);
        try {
            SensitiveActionAuthorizationService.IssuedAuthorization issued = service.authorize(
                    masterPassword, scope, actorId(actor), classroomId, targets, action);
            return new AuthorizationResponse(issued.token(), issued.expiresAtUtc().toString());
        } finally {
            CredentialVaultController.clear(body);
        }
    }

    private List<String> parseTargets(String raw) {
        if (raw == null || raw.isBlank()) {
            throw new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST,
                    TARGETS_HEADER + " is required.");
        }
        return Arrays.stream(raw.split(",", -1)).map(String::trim).toList();
    }

    public static String actorId(MasterAuthorizationResponse actor) {
        String value = actor.boundAccountDisplayName();
        if (value == null || value.isBlank()) value = actor.currentAccountDisplayName();
        return value == null || value.isBlank() ? "LOCAL_MASTER" : value.trim();
    }

    public record AuthorizationResponse(String sensitiveAuthorizationToken, String expiresAtUtc) {}
}
