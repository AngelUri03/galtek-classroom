package com.galtek.classroom.windows;

import com.galtek.classroom.credentialvault.CredentialVaultController;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountCredentialProvisionResponse;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountStatusResponse;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountBindingRequest;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountMutationResponse;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.MediaType;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

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

    public ManagedAccountAdminController(ManagedAccountAdminService service) {
        this.service = service;
    }

    @GetMapping("/classrooms/{classroomId}/devices/{deviceId}/managed-accounts")
    public ManagedAccountStatusResponse status(
            @PathVariable String classroomId,
            @PathVariable String deviceId) {
        return service.status(classroomId, deviceId);
    }

    @PutMapping("/classrooms/{classroomId}/devices/{deviceId}/managed-accounts/{accountId}")
    public ManagedAccountMutationResponse bind(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @PathVariable String accountId,
            @RequestBody ManagedAccountBindingRequest request) {
        return service.bind(classroomId, deviceId, accountId,
                request == null ? null : request.windowsAccountName());
    }

    @DeleteMapping("/classrooms/{classroomId}/devices/{deviceId}/managed-accounts/{accountId}")
    public ManagedAccountMutationResponse unbind(
            @PathVariable String classroomId,
            @PathVariable String deviceId,
            @PathVariable String accountId) {
        return service.unbind(classroomId, deviceId, accountId);
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
            return service.configureCredential(
                    classroomId,
                    deviceId,
                    accountId,
                    vaultSessionToken,
                    password);
        } finally {
            CredentialVaultController.clear(body);
        }
    }
}
