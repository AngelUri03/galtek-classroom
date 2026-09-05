package com.galtek.classroom.credentialvault;

import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientConnectionSnapshot;
import com.galtek.classroom.network.MasterRemoteOperationGateway;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.windows.ManagedWindowsAccountType;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.Optional;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class ManagedCredentialProvisioningBridge {

    private final MasterAccessGuard masterAccessGuard;
    private final CredentialVaultService credentialVaultService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway remoteOperationGateway;

    public ManagedCredentialProvisioningBridge(
            MasterAccessGuard masterAccessGuard,
            CredentialVaultService credentialVaultService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway remoteOperationGateway) {
        this.masterAccessGuard = masterAccessGuard;
        this.credentialVaultService = credentialVaultService;
        this.connectionRegistry = connectionRegistry;
        this.remoteOperationGateway = remoteOperationGateway;
    }

    public RemoteOperationOutcome provision(
            String vaultSessionToken,
            String credentialId,
            String deviceId,
            String operationId,
            ManagedWindowsAccountType accountId) {
        masterAccessGuard.requireAuthorized();
        String cleanDeviceId = required(deviceId, "deviceId");
        String cleanOperationId = required(operationId, "operationId");
        String cleanCredentialId = required(credentialId, "credentialId");
        ManagedWindowsAccountId networkAccountId = toNetworkAccountId(accountId);

        CredentialVaultEntryMetadata metadata = credentialVaultService.list(vaultSessionToken).stream()
                .filter(entry -> entry.credentialId().equals(cleanCredentialId))
                .findFirst()
                .orElseThrow(ManagedCredentialProvisioningBridge::credentialNotFound);
        if (metadata.credentialType() != CredentialType.WINDOWS_ACCOUNT) {
            throw notProvisionable();
        }

        ClientConnectionSnapshot snapshot = connectionRegistry.findByDeviceId(cleanDeviceId).orElse(null);
        if (!readyForProvisioning(snapshot, cleanDeviceId)) {
            return RemoteOperationOutcome.failed(ErrorCode.DEVICE_OFFLINE, "Device is offline.");
        }
        if (!snapshot.capabilities().contains(DeviceCapability.MANAGED_CREDENTIAL_PROVISIONING_V1)) {
            return RemoteOperationOutcome.failed(
                    ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    "Device does not announce MANAGED_CREDENTIAL_PROVISIONING_V1.");
        }

        CredentialVaultEntry entry = credentialVaultService.readInternal(vaultSessionToken, cleanCredentialId);
        if (entry.credentialType() != CredentialType.WINDOWS_ACCOUNT) {
            throw notProvisionable();
        }

        byte[] passwordUtf16Le = entry.password().getBytes(StandardCharsets.UTF_16LE);
        try {
            Optional<DispatchHandle> handle = remoteOperationGateway.provisionManagedCredential(
                    snapshot,
                    cleanOperationId,
                    cleanDeviceId,
                    networkAccountId,
                    passwordUtf16Le);
            if (handle.isEmpty()) {
                return RemoteOperationOutcome.failed(ErrorCode.DEVICE_OFFLINE, "Device is offline.");
            }
            return awaitOutcome(handle.get());
        } finally {
            Arrays.fill(passwordUtf16Le, (byte) 0);
        }
    }

    private RemoteOperationOutcome awaitOutcome(DispatchHandle handle) {
        try {
            return handle.completion().get(
                    remoteOperationGateway.resultTimeout().toNanos(),
                    TimeUnit.NANOSECONDS);
        } catch (TimeoutException exception) {
            return remoteOperationGateway.timeout(handle);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return remoteOperationGateway.timeout(handle);
        } catch (ExecutionException exception) {
            return remoteOperationGateway.timeout(handle);
        }
    }

    private static boolean readyForProvisioning(ClientConnectionSnapshot snapshot, String deviceId) {
        return snapshot != null
                && snapshot.registered()
                && snapshot.status() == DeviceStatus.ONLINE
                && snapshot.deviceId() != null
                && snapshot.deviceId().equals(deviceId)
                && snapshot.connectionId() != null;
    }

    private static ManagedWindowsAccountId toNetworkAccountId(ManagedWindowsAccountType accountId) {
        if (accountId == ManagedWindowsAccountType.PRIMARY) {
            return ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY;
        }
        if (accountId == ManagedWindowsAccountType.SECONDARY) {
            return ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY;
        }
        throw new CredentialVaultException(ErrorCode.INVALID_REQUEST, "Managed Windows account id is required.");
    }

    private static String required(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw new CredentialVaultException(ErrorCode.INVALID_REQUEST, fieldName + " is required.");
        }
        return value.trim();
    }

    private static CredentialVaultException credentialNotFound() {
        return new CredentialVaultException(ErrorCode.CREDENTIAL_NOT_FOUND, "Credential was not found.");
    }

    private static CredentialVaultException notProvisionable() {
        return new CredentialVaultException(
                ErrorCode.CREDENTIAL_NOT_PROVISIONABLE,
                "Credential is not provisionable as a managed Windows account.");
    }
}
