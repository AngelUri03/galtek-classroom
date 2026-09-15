package com.galtek.classroom.windows;

import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
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
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountCredentialProvisionResponse;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountSlotResponse;
import com.galtek.classroom.windows.ManagedAccountAdminDtos.ManagedAccountStatusResponse;
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
            ManagedCredentialProvisioningBridge provisioningBridge) {
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
    }

    public ManagedAccountStatusResponse status(String classroomId, String deviceId) {
        requireAuthorizedAndStorage();
        TargetPlan target = preflightStatusTarget(classroomId, deviceId);
        ManagedAccountStatusResult status = queryManagedAccountStatus(target);
        return ManagedAccountStatusResponse.from(status);
    }

    public ManagedAccountCredentialProvisionResponse configureCredential(
            String classroomId,
            String deviceId,
            String accountId,
            String vaultSessionToken,
            String password) {
        requireAuthorizedAndStorage();
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

        String credentialId = upsertVaultCredential(
                vaultSessionToken,
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

        return new ManagedAccountCredentialProvisionResponse(
                accountType.name(),
                provisioningStatus(provisioning),
                operationId,
                provisioning.errorCode() == null ? null : provisioning.errorCode().name(),
                provisioning.message(),
                observedAccount);
    }

    private String upsertVaultCredential(
            String vaultSessionToken,
            String windowsAccountName,
            String password) {
        List<CredentialVaultEntryMetadata> matches = credentialVaultService.list(vaultSessionToken).stream()
                .filter(entry -> entry.credentialType() == CredentialType.WINDOWS_ACCOUNT)
                .filter(entry -> windowsAccountName.equals(entry.loginIdentifier()))
                .toList();
        if (matches.size() > 1) {
            throw new ApiException(
                    HttpStatus.CONFLICT,
                    ErrorCode.CREDENTIAL_NOT_PROVISIONABLE,
                    "Multiple vault credentials match the managed Windows account.");
        }

        String displayName = REQUESTED_DISPLAY_PREFIX + windowsAccountName;
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
                    "accountId must be PRIMARY or SECONDARY.");
        }
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
}
