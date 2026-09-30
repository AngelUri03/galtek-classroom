package com.galtek.classroom.windows;

import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.activity.DeviceActivityRecorder;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.credentialvault.CredentialType;
import com.galtek.classroom.credentialvault.CredentialVaultEntryDraft;
import com.galtek.classroom.credentialvault.CredentialVaultEntryMetadata;
import com.galtek.classroom.credentialvault.CredentialVaultEntryUpdate;
import com.galtek.classroom.credentialvault.CredentialVaultService;
import com.galtek.classroom.credentialvault.ManagedCredentialProvisioningBridge;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientConnectionSnapshot;
import com.galtek.classroom.network.DeviceNetworkBindingRepository;
import com.galtek.classroom.network.KnownMasterClient;
import com.galtek.classroom.network.MasterPairingService;
import com.galtek.classroom.network.MasterRemoteOperationGateway;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.PairingStatus;
import com.galtek.classroom.network.RegisteredNetworkDevice;
import com.galtek.classroom.network.v1.ManagedAccountStatusResult;
import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import com.galtek.classroom.security.SensitiveActionAuthorizationController;
import com.galtek.classroom.security.SensitiveActionAuthorizationService;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountCredentialProvisionResponse;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountSlotResponse;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountStatusResponse;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountMutationResponse;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class ManagedAccountAdminService {

    private static final String REQUESTED_DISPLAY_PREFIX = "Managed Windows account ";

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterAdminRepository adminRepository;
    private final DeviceRepository deviceRepository;
    private final DeviceNetworkBindingRepository bindingRepository;
    private final MasterPairingService pairingService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway remoteOperationGateway;
    private final CredentialVaultService credentialVaultService;
    private final ManagedCredentialProvisioningBridge provisioningBridge;
    private final SensitiveActionAuthorizationService sensitiveAuthorizationService;
    private final DeviceActivityRecorder activityRecorder;

    public ManagedAccountAdminService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterAdminRepository adminRepository,
            DeviceRepository deviceRepository,
            DeviceNetworkBindingRepository bindingRepository,
            MasterPairingService pairingService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway remoteOperationGateway,
            CredentialVaultService credentialVaultService,
            ManagedCredentialProvisioningBridge provisioningBridge,
            SensitiveActionAuthorizationService sensitiveAuthorizationService,
            DeviceActivityRecorder activityRecorder) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.adminRepository = adminRepository;
        this.deviceRepository = deviceRepository;
        this.bindingRepository = bindingRepository;
        this.pairingService = pairingService;
        this.connectionRegistry = connectionRegistry;
        this.remoteOperationGateway = remoteOperationGateway;
        this.credentialVaultService = credentialVaultService;
        this.provisioningBridge = provisioningBridge;
        this.sensitiveAuthorizationService = sensitiveAuthorizationService;
        this.activityRecorder = activityRecorder;
    }

    public ManagedAccountStatusResponse status(String classroomId, String deviceId) {
        return status(classroomId, deviceId, null);
    }

    public ManagedAccountStatusResponse status(
            String classroomId,
            String deviceId,
            String vaultSessionToken) {
        requireAuthorizedAndStorage();
        TargetPlan target = preflightStatusTarget(classroomId, deviceId);
        ManagedAccountStatusResult status = queryManagedAccountStatus(target);
        ManagedAccountStatusResponse response = ManagedAccountStatusResponse.from(status);
        if (vaultSessionToken == null || vaultSessionToken.isBlank()) return response;
        List<CredentialVaultEntryMetadata> vaultEntries = credentialVaultService.list(vaultSessionToken);
        return new ManagedAccountStatusResponse(response.accounts().stream()
                .map(account -> account.withVaultCredential(vaultEntries.stream().anyMatch(entry ->
                        matchesManagedCredential(entry, target.deviceId(), account.windowsAccountName()))))
                .toList());
    }

    public ManagedAccountMutationResponse bind(
            String classroomId,
            String deviceId,
            String accountId,
            String windowsAccountName) {
        MasterAuthorizationResponse actor = requireAuthorizedAndStorage();
        ManagedWindowsAccountType accountType = parseAccountId(accountId);
        String cleanAccountName = required(windowsAccountName, "windowsAccountName");
        if (cleanAccountName.length() > 256 || cleanAccountName.chars().anyMatch(Character::isISOControl)) {
            throw new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST,
                    "windowsAccountName is invalid.");
        }
        TargetPlan target = preflightBindingTarget(classroomId, deviceId);
        String operationId = UUID.randomUUID().toString();
        RemoteOperationOutcome outcome = remoteOperationGateway.setManagedAccountBinding(
                        target.snapshot(), operationId, target.deviceId(),
                        toNetworkAccountId(accountType), cleanAccountName)
                .map(handle -> awaitOutcome(handle, remoteOperationGateway.resultTimeout()))
                .orElseGet(() -> RemoteOperationOutcome.failed(ErrorCode.DEVICE_OFFLINE, "Device is offline."));
        ManagedAccountSlotResponse observed = null;
        if (outcome.status() == TargetExecutionStatus.SUCCESS) {
            observed = ManagedAccountAdminDtos.requireAccount(queryManagedAccountStatus(target), accountType);
        }
        record(classroomId, target.deviceId(), "MANAGED_PROFILE_BOUND", actor, accountType,
                cleanAccountName, outcome);
        return mutationResponse(accountType, operationId, outcome, observed);
    }

    public ManagedAccountMutationResponse unbind(
            String classroomId,
            String deviceId,
            String accountId,
            String vaultSessionToken) {
        MasterAuthorizationResponse actor = requireAuthorizedAndStorage();
        ManagedWindowsAccountType accountType = parseAccountId(accountId);
        TargetPlan target = preflightBindingTarget(classroomId, deviceId);
        ManagedAccountSlotResponse before = ManagedAccountAdminDtos.requireAccount(
                queryManagedAccountStatus(target), accountType);
        String vaultCredentialId = before.configured()
                ? findVaultCredential(vaultSessionToken, target.deviceId(), before.windowsAccountName())
                : null;
        String operationId = UUID.randomUUID().toString();
        RemoteOperationOutcome outcome = remoteOperationGateway.removeManagedAccountBinding(
                        target.snapshot(), operationId, target.deviceId(), toNetworkAccountId(accountType))
                .map(handle -> awaitOutcome(handle, remoteOperationGateway.resultTimeout()))
                .orElseGet(() -> RemoteOperationOutcome.failed(ErrorCode.DEVICE_OFFLINE, "Device is offline."));
        ManagedAccountSlotResponse observed = null;
        if (outcome.status() == TargetExecutionStatus.SUCCESS) {
            if (vaultCredentialId != null) {
                credentialVaultService.remove(vaultSessionToken, vaultCredentialId);
            }
            observed = ManagedAccountAdminDtos.requireAccount(queryManagedAccountStatus(target), accountType);
        }
        record(classroomId, target.deviceId(), "MANAGED_PROFILE_UNBOUND", actor, accountType,
                before.windowsAccountName(), outcome);
        return mutationResponse(accountType, operationId, outcome, observed);
    }

    public ManagedAccountCredentialProvisionResponse configureCredential(
            String classroomId,
            String deviceId,
            String accountId,
            String vaultSessionToken,
            String password) {
        MasterAuthorizationResponse actor = requireAuthorizedAndStorage();
        ManagedWindowsAccountType accountType = parseAccountId(accountId);
        TargetPlan target = preflightStatusTarget(classroomId, deviceId);
        if (!target.snapshot().capabilities().contains(DeviceCapability.MANAGED_CREDENTIAL_PROVISIONING_V1)) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    "Device does not announce MANAGED_CREDENTIAL_PROVISIONING_V1.");
        }

        ManagedAccountStatusResult beforeStatus = queryManagedAccountStatus(target);
        ManagedAccountSlotResponse beforeAccount = ManagedAccountAdminDtos.requireAccount(beforeStatus, accountType);
        if (!beforeAccount.configured()) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.ACCOUNT_NOT_CONFIGURED,
                    "Managed Windows account is not configured on the target device.");
        }
        if (beforeAccount.windowsAccountName() == null || beforeAccount.windowsAccountName().isBlank()) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.ACCOUNT_NOT_FOUND,
                    "Managed Windows account name is not available on the target device.");
        }

        boolean updating = findVaultCredential(
                vaultSessionToken, target.deviceId(), beforeAccount.windowsAccountName()) != null;
        String credentialId = upsertVaultCredential(
                vaultSessionToken,
                target.deviceId(),
                beforeAccount.windowsAccountName(),
                password);
        String operationId = UUID.randomUUID().toString();
        RemoteOperationOutcome provisioning = provisioningBridge.provision(
                vaultSessionToken,
                credentialId,
                target.deviceId(),
                operationId,
                accountType);

        ManagedAccountSlotResponse observedAccount = beforeAccount;
        if (provisioning.status() == TargetExecutionStatus.SUCCESS) {
            ManagedAccountStatusResult refreshed = queryManagedAccountStatus(target);
            observedAccount = ManagedAccountAdminDtos.requireAccount(refreshed, accountType);
        }

        record(classroomId, target.deviceId(),
                updating ? "MANAGED_CREDENTIAL_UPDATED" : "MANAGED_CREDENTIAL_REGISTERED",
                actor, accountType, beforeAccount.windowsAccountName(), provisioning);
        return new ManagedAccountCredentialProvisionResponse(
                accountType.name(),
                provisioningStatus(provisioning),
                operationId,
                provisioning.errorCode() == null ? null : provisioning.errorCode().name(),
                provisioning.message(),
                observedAccount);
    }

    public ManagedAccountMutationResponse removeCredential(
            String classroomId,
            String deviceId,
            String accountId,
            String vaultSessionToken) {
        MasterAuthorizationResponse actor = requireAuthorizedAndStorage();
        ManagedWindowsAccountType accountType = parseAccountId(accountId);
        TargetPlan target = preflightStatusTarget(classroomId, deviceId);
        if (!target.snapshot().capabilities().contains(DeviceCapability.MANAGED_CREDENTIAL_REMOVAL_V1)) {
            throw new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    "Device does not announce MANAGED_CREDENTIAL_REMOVAL_V1.");
        }

        ManagedAccountSlotResponse before = ManagedAccountAdminDtos.requireAccount(
                queryManagedAccountStatus(target), accountType);
        if (!before.configured()) {
            throw new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.ACCOUNT_NOT_CONFIGURED,
                    "Managed Windows account is not configured on the target device.");
        }
        String vaultCredentialId = findVaultCredential(
                vaultSessionToken, target.deviceId(), before.windowsAccountName());

        String operationId = UUID.randomUUID().toString();
        RemoteOperationOutcome outcome = remoteOperationGateway.removeManagedCredential(
                        target.snapshot(), operationId, target.deviceId(), toNetworkAccountId(accountType))
                .map(handle -> awaitOutcome(handle, remoteOperationGateway.resultTimeout()))
                .orElseGet(() -> RemoteOperationOutcome.failed(ErrorCode.DEVICE_OFFLINE, "Device is offline."));

        ManagedAccountSlotResponse observed = before;
        if (outcome.status() == TargetExecutionStatus.SUCCESS) {
            if (vaultCredentialId != null) {
                credentialVaultService.remove(vaultSessionToken, vaultCredentialId);
            }
            observed = ManagedAccountAdminDtos.requireAccount(queryManagedAccountStatus(target), accountType);
        }
        record(classroomId, target.deviceId(), "MANAGED_CREDENTIAL_REMOVED", actor, accountType,
                before.windowsAccountName(), outcome);
        return mutationResponse(accountType, operationId, outcome, observed);
    }

    public CredentialReveal revealCredential(
            String classroomId,
            String deviceId,
            String accountId,
            String sensitiveAuthorization) {
        MasterAuthorizationResponse actor = requireAuthorizedAndStorage();
        ManagedWindowsAccountType accountType = parseAccountId(accountId);
        TargetPlan target = preflightStatusTarget(classroomId, deviceId);
        ManagedAccountSlotResponse account = ManagedAccountAdminDtos.requireAccount(
                queryManagedAccountStatus(target), accountType);
        if (!account.configured() || account.windowsAccountName() == null) {
            throw new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.ACCOUNT_NOT_CONFIGURED,
                    "Managed Windows account is not configured on the target device.");
        }
        String displayNamePrefix = REQUESTED_DISPLAY_PREFIX + target.deviceId() + " / ";
        try {
            var revealed = sensitiveAuthorizationService.revealManagedCredential(
                    sensitiveAuthorization,
                    SensitiveActionAuthorizationController.actorId(actor),
                    classroomOr404(classroomId).classroomId(),
                    target.deviceId(),
                    displayNamePrefix,
                    account.windowsAccountName());
            return new CredentialReveal(
                    revealed.password(), revealed.credentialId(),
                    SensitiveActionAuthorizationController.actorId(actor), accountType,
                    account.windowsAccountName());
        } catch (RuntimeException exception) {
            activityRecorder.record(classroomId, target.deviceId(), "MANAGED_CREDENTIAL_REVEALED",
                    SensitiveActionAuthorizationController.actorId(actor), accountType.name(),
                    account.windowsAccountName(), "FAILED", "Credential reveal was rejected.");
            throw exception;
        }
    }

    public void recordCredentialRevealDelivered(
            String classroomId,
            String deviceId,
            CredentialReveal reveal) {
        sensitiveAuthorizationService.auditDeliveredCredentialReveal(reveal.credentialId());
        activityRecorder.record(classroomId, deviceId, "MANAGED_CREDENTIAL_REVEALED",
                reveal.actor(), reveal.accountType().name(), reveal.accountReference(), "SUCCESS", null);
    }

    public void recordCredentialRevealDeliveryFailed(
            String classroomId,
            String deviceId,
            CredentialReveal reveal) {
        activityRecorder.record(classroomId, deviceId, "MANAGED_CREDENTIAL_REVEALED",
                reveal.actor(), reveal.accountType().name(), reveal.accountReference(), "FAILED",
                "Credential response delivery failed.");
    }

    private String findVaultCredential(String vaultSessionToken, String deviceId, String windowsAccountName) {
        if (windowsAccountName == null || windowsAccountName.isBlank()) {
            credentialVaultService.list(vaultSessionToken);
            return null;
        }
        List<CredentialVaultEntryMetadata> matches = credentialVaultService.list(vaultSessionToken).stream()
                .filter(entry -> matchesManagedCredential(entry, deviceId, windowsAccountName))
                .toList();
        if (matches.size() > 1) {
            throw new ApiException(HttpStatus.CONFLICT, ErrorCode.CREDENTIAL_NOT_PROVISIONABLE,
                    "Multiple vault credentials match the managed Windows account.");
        }
        return matches.isEmpty() ? null : matches.getFirst().credentialId();
    }

    private String upsertVaultCredential(
            String vaultSessionToken,
            String deviceId,
            String windowsAccountName,
            String password) {
        String displayName = REQUESTED_DISPLAY_PREFIX + deviceId + " / " + windowsAccountName;
        List<CredentialVaultEntryMetadata> matches = credentialVaultService.list(vaultSessionToken).stream()
                .filter(entry -> matchesManagedCredential(entry, deviceId, windowsAccountName))
                .toList();
        if (matches.size() > 1) {
            throw new ApiException(
                    HttpStatus.CONFLICT,
                    ErrorCode.CREDENTIAL_NOT_PROVISIONABLE,
                    "Multiple vault credentials match the managed Windows account.");
        }

        if (matches.isEmpty()) {
            CredentialVaultEntryMetadata created = credentialVaultService.add(
                    vaultSessionToken,
                    new CredentialVaultEntryDraft(
                            CredentialType.WINDOWS_ACCOUNT,
                            displayName,
                            windowsAccountName,
                            password));
            return created.credentialId();
        }

        CredentialVaultEntryMetadata existing = matches.getFirst();
        credentialVaultService.update(
                vaultSessionToken,
                existing.credentialId(),
                new CredentialVaultEntryUpdate(
                        displayName,
                        windowsAccountName,
                        password));
        return existing.credentialId();
    }

    private ManagedAccountStatusResult queryManagedAccountStatus(TargetPlan target) {
        String operationId = UUID.randomUUID().toString();
        RemoteOperationOutcome outcome = remoteOperationGateway.getManagedAccountStatus(
                        target.snapshot(),
                        operationId,
                        target.deviceId())
                .map(handle -> awaitOutcome(handle, remoteOperationGateway.resultTimeout()))
                .orElseGet(() -> RemoteOperationOutcome.failed(
                        ErrorCode.DEVICE_OFFLINE,
                        "Device is offline."));
        if (outcome.status() != TargetExecutionStatus.SUCCESS || outcome.managedAccountStatus() == null) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    outcome.errorCode() == null ? ErrorCode.OPERATION_RESULT_UNKNOWN : outcome.errorCode(),
                    outcome.message());
        }
        return outcome.managedAccountStatus();
    }

    private boolean matchesManagedCredential(
            CredentialVaultEntryMetadata entry,
            String deviceId,
            String windowsAccountName) {
        if (windowsAccountName == null || windowsAccountName.isBlank()) return false;
        String displayPrefix = REQUESTED_DISPLAY_PREFIX + deviceId + " / ";
        return entry.credentialType() == CredentialType.WINDOWS_ACCOUNT
                && entry.displayName().startsWith(displayPrefix)
                && localAccountName(entry.loginIdentifier())
                        .equalsIgnoreCase(localAccountName(windowsAccountName));
    }

    private String localAccountName(String accountName) {
        int separator = accountName.lastIndexOf('\\');
        return separator >= 0 ? accountName.substring(separator + 1) : accountName;
    }

    private RemoteOperationOutcome awaitOutcome(DispatchHandle handle, java.time.Duration timeout) {
        try {
            return handle.completion().get(timeout.toNanos(), TimeUnit.NANOSECONDS);
        } catch (TimeoutException exception) {
            return remoteOperationGateway.timeout(handle);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return remoteOperationGateway.timeout(handle);
        } catch (ExecutionException exception) {
            return remoteOperationGateway.timeout(handle);
        }
    }

    private TargetPlan preflightStatusTarget(String classroomId, String deviceId) {
        ClassroomResponse classroom = classroomOr404(classroomId);
        Map<String, Device> devices = deviceRepository.findByClassroomId(classroom.classroomId()).stream()
                .collect(Collectors.toMap(
                        Device::deviceId,
                        Function.identity(),
                        (left, right) -> left));
        Device device = devices.get(required(deviceId, "deviceId"));
        if (device == null) {
            throw new ApiException(
                    HttpStatus.NOT_FOUND,
                    ErrorCode.DEVICE_NOT_FOUND,
                    "Device does not belong to classroom.");
        }

        RegisteredNetworkDevice binding = bindingRepository.findCurrentByDeviceIds(List.of(device.deviceId())).stream()
                .findFirst()
                .orElseThrow(() -> new ApiException(
                        HttpStatus.BAD_REQUEST,
                        ErrorCode.DEVICE_NOT_REGISTERED,
                        "Device is not registered for network operations."));
        trustOrThrow(binding);

        ClientConnectionSnapshot snapshot = connectionRegistry.findByDeviceId(device.deviceId()).orElse(null);
        if (!authenticatedOnline(snapshot, binding)) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.DEVICE_OFFLINE,
                    "Device is offline.");
        }
        if (!snapshot.capabilities().contains(DeviceCapability.MANAGED_ACCOUNT_STATUS_V1)) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    "Device does not announce MANAGED_ACCOUNT_STATUS_V1.");
        }

        return new TargetPlan(device.deviceId(), snapshot);
    }

    private TargetPlan preflightBindingTarget(String classroomId, String deviceId) {
        TargetPlan target = preflightStatusTarget(classroomId, deviceId);
        if (!target.snapshot().capabilities().contains(DeviceCapability.MANAGED_ACCOUNT_BINDING_V2)) {
            throw new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    "Device does not announce MANAGED_ACCOUNT_BINDING_V2.");
        }
        return target;
    }

    private void trustOrThrow(RegisteredNetworkDevice binding) {
        Map<UUID, KnownMasterClient> knownClients = pairingService.knownClients().stream()
                .collect(Collectors.toMap(
                        KnownMasterClient::clientNetworkIdentityId,
                        Function.identity(),
                        (left, right) -> left));
        KnownMasterClient client = knownClients.get(binding.networkIdentityId());
        if (client != null && client.status() == PairingStatus.REVOKED) {
            throw new ApiException(
                    HttpStatus.FORBIDDEN,
                    ErrorCode.CLIENT_REVOKED,
                    "Client pairing has been revoked.");
        }
        if (client == null || client.status() != PairingStatus.PAIRED
                || !client.clientInstallationId().equals(binding.installationId())
                || !client.clientPublicKeyFingerprint().equals(binding.publicKeyFingerprint())) {
            throw new ApiException(
                    HttpStatus.FORBIDDEN,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client is not paired with this Master.");
        }
    }

    private boolean authenticatedOnline(
            ClientConnectionSnapshot snapshot,
            RegisteredNetworkDevice binding) {
        return snapshot != null
                && snapshot.registered()
                && snapshot.status() == DeviceStatus.ONLINE
                && snapshot.clientNetworkIdentityId().equals(binding.networkIdentityId())
                && snapshot.clientInstallationId().equals(binding.installationId())
                && snapshot.deviceId() != null
                && snapshot.deviceId().equals(binding.deviceId())
                && snapshot.connectionId() != null;
    }

    private ManagedWindowsAccountType parseAccountId(String accountId) {
        String clean = required(accountId, "accountId");
        try {
            return ManagedWindowsAccountType.valueOf(clean);
        } catch (IllegalArgumentException exception) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.INVALID_REQUEST,
                    "accountId must be PRIMARY, SECONDARY or ADMIN.");
        }
    }

    private ManagedWindowsAccountId toNetworkAccountId(ManagedWindowsAccountType accountType) {
        return switch (accountType) {
            case PRIMARY -> ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY;
            case SECONDARY -> ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY;
            case ADMIN -> ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_ADMIN;
        };
    }

    private ManagedAccountMutationResponse mutationResponse(
            ManagedWindowsAccountType accountType,
            String operationId,
            RemoteOperationOutcome outcome,
            ManagedAccountSlotResponse observed) {
        return new ManagedAccountMutationResponse(
                accountType.name(),
                outcome.status().name(),
                operationId,
                outcome.errorCode() == null ? null : outcome.errorCode().name(),
                outcome.message(),
                observed);
    }

    private String provisioningStatus(RemoteOperationOutcome outcome) {
        if (outcome.status() == TargetExecutionStatus.SUCCESS) {
            return TargetExecutionStatus.SUCCESS.name();
        }
        if (outcome.errorCode() == ErrorCode.OPERATION_RESULT_UNKNOWN) {
            return ErrorCode.OPERATION_RESULT_UNKNOWN.name();
        }
        return outcome.status().name();
    }

    private void record(
            String classroomId,
            String deviceId,
            String eventType,
            MasterAuthorizationResponse actor,
            ManagedWindowsAccountType role,
            String accountReference,
            RemoteOperationOutcome outcome) {
        activityRecorder.record(classroomId, deviceId, eventType,
                SensitiveActionAuthorizationController.actorId(actor), role.name(), accountReference,
                outcome.status().name(), outcome.message());
    }

    private ClassroomResponse classroomOr404(String classroomId) {
        String cleanId = required(classroomId, "classroomId");
        return adminRepository.findClassroom(cleanId)
                .orElseThrow(() -> new ApiException(
                        HttpStatus.NOT_FOUND,
                        ErrorCode.CLASSROOM_NOT_FOUND,
                        "Classroom was not found."));
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

    private String required(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.INVALID_REQUEST,
                    fieldName + " is required.");
        }
        return value.trim();
    }

    private record TargetPlan(
            String deviceId,
            ClientConnectionSnapshot snapshot) {
    }

    public record CredentialReveal(
            String password,
            String credentialId,
            String actor,
            ManagedWindowsAccountType accountType,
            String accountReference) {
    }
}
