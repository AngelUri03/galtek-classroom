package com.galtek.classroom.windows;

import com.galtek.classroom.credentialvault.CredentialVaultController;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountCredentialProvisionResponse;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountStatusResponse;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountBindingRequest;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountMutationResponse;
import com.galtek.classroom.operations.DeviceMutationHttpGuard;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.MediaType;
import org.springframework.http.CacheControl;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.servlet.mvc.method.annotation.StreamingResponseBody;

@RestController
@RequestMapping("/api")
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class ManagedAccountAdminController {

    private static final int MAX_WINDOWS_PASSWORD_BYTES = 32 * 1024;

    private final ManagedAccountAdminService service;
    private final DeviceMutationHttpGuard mutationGuard;

    public ManagedAccountAdminController(
            ManagedAccountAdminService service,
            DeviceMutationHttpGuard mutationGuard) {
        this.service = service;
        this.mutationGuard = mutationGuard;
    }

    @GetMapping("/classrooms/{classroomId}/devices/{deviceId}/managed-accounts")
    public ManagedAccountStatusResponse status(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @RequestHeader(value = CredentialVaultController.VAULT_SESSION_HEADER, required = false)
                    String vaultSessionToken) {
        ManagedAccountStatusResponse response = service.status(classroomId, deviceId, vaultSessionToken);
        mutationGuard.confirmStable(
                java.util.List.of(deviceId),
                java.util.List.of(
                        "BIND_MANAGED_ACCOUNT",
                        "UNBIND_MANAGED_ACCOUNT",
                        "CONFIGURE_MANAGED_CREDENTIAL",
                        "REMOVE_MANAGED_CREDENTIAL"));
        return response;
    }

    @PutMapping("/classrooms/{classroomId}/devices/{deviceId}/managed-accounts/{accountId}")
    public ManagedAccountMutationResponse bind(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @PathVariable String accountId,
            @RequestBody ManagedAccountBindingRequest request) {
        return mutationGuard.run(
                java.util.List.of(deviceId), "BIND_MANAGED_ACCOUNT", accountId,
                () -> service.bind(classroomId, deviceId, accountId,
                        request == null ? null : request.windowsAccountName()),
                response -> java.util.List.of(response.status()));
    }

    @DeleteMapping("/classrooms/{classroomId}/devices/{deviceId}/managed-accounts/{accountId}")
    public ManagedAccountMutationResponse unbind(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @PathVariable String accountId,
            @RequestHeader(CredentialVaultController.VAULT_SESSION_HEADER) String vaultSessionToken) {
        return mutationGuard.run(
                java.util.List.of(deviceId), "UNBIND_MANAGED_ACCOUNT", accountId,
                () -> service.unbind(classroomId, deviceId, accountId, vaultSessionToken),
                response -> java.util.List.of(response.status()));
    }

    @PutMapping(
            path = "/classrooms/{classroomId}/devices/{deviceId}/managed-accounts/{accountId}/credential",
            consumes = MediaType.APPLICATION_OCTET_STREAM_VALUE)
    public ManagedAccountCredentialProvisionResponse configureCredential(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @PathVariable String accountId,
            @RequestHeader(CredentialVaultController.VAULT_SESSION_HEADER) String vaultSessionToken,
            @RequestBody byte[] body) {
        String password = CredentialVaultController.decodeUtf8Secret(body, MAX_WINDOWS_PASSWORD_BYTES);
        try {
            return mutationGuard.run(
                    java.util.List.of(deviceId), "CONFIGURE_MANAGED_CREDENTIAL", accountId,
                    () -> service.configureCredential(
                            classroomId,
                            deviceId,
                            accountId,
                            vaultSessionToken,
                            password),
                    response -> java.util.List.of(response.provisioningStatus()));
        } finally {
            CredentialVaultController.clear(body);
        }
    }

    @DeleteMapping("/classrooms/{classroomId}/devices/{deviceId}/managed-accounts/{accountId}/credential")
    public ManagedAccountMutationResponse removeCredential(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @PathVariable String accountId,
            @RequestHeader(CredentialVaultController.VAULT_SESSION_HEADER) String vaultSessionToken) {
        return mutationGuard.run(
                java.util.List.of(deviceId), "REMOVE_MANAGED_CREDENTIAL", accountId,
                () -> service.removeCredential(classroomId, deviceId, accountId, vaultSessionToken),
                response -> java.util.List.of(response.status()));
    }

    @PostMapping("/classrooms/{classroomId}/devices/{deviceId}/managed-accounts/{accountId}/credential/reveal")
    public ResponseEntity<StreamingResponseBody> revealCredential(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @PathVariable String accountId,
            @RequestHeader(com.galtek.classroom.security.SensitiveActionAuthorizationService.HEADER)
                    String sensitiveAuthorization) {
        ManagedAccountAdminService.CredentialReveal reveal = service.revealCredential(
                classroomId, deviceId, accountId, sensitiveAuthorization);
        byte[] body = reveal.password().getBytes(java.nio.charset.StandardCharsets.UTF_8);
        StreamingResponseBody responseBody = output -> {
            try {
                output.write(body);
                output.flush();
                service.recordCredentialRevealDelivered(classroomId, deviceId, reveal);
            } catch (java.io.IOException exception) {
                service.recordCredentialRevealDeliveryFailed(classroomId, deviceId, reveal);
                throw exception;
            } finally {
                java.util.Arrays.fill(body, (byte) 0);
            }
        };
        return ResponseEntity.ok()
                .contentType(MediaType.APPLICATION_OCTET_STREAM)
                .cacheControl(CacheControl.noStore())
                .header("Pragma", "no-cache")
                .body(responseBody);
    }
}
