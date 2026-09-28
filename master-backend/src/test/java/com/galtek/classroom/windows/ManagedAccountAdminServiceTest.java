package com.galtek.classroom.windows;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.inOrder;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.galtek.classroom.admin.AdminDtos.ClassroomCounts;
import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.credentialvault.CredentialType;
import com.galtek.classroom.credentialvault.CredentialVaultEntryDraft;
import com.galtek.classroom.credentialvault.CredentialVaultEntryMetadata;
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
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationKey;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.PairingStatus;
import com.galtek.classroom.network.RegisteredNetworkDevice;
import com.galtek.classroom.network.v1.ManagedAccountCredentialStatus;
import com.galtek.classroom.network.v1.ManagedAccountStatus;
import com.galtek.classroom.network.v1.ManagedAccountStatusResult;
import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.EnumSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.Test;
import org.mockito.InOrder;

class ManagedAccountAdminServiceTest {

    private static final String CLASSROOM_ID = "classroom-1";
    private static final String DEVICE_ID = "device-1";
    private static final UUID INSTALLATION_ID = UUID.fromString("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static final UUID NETWORK_IDENTITY_ID = UUID.fromString("11111111-2222-3333-4444-555555555555");
    private static final String FINGERPRINT = "fingerprint";

    @Test
    void configureCredentialStoresInVaultBeforeProvisioningAndRefreshesStatusOnSuccess() {
        Fixture fixture = new Fixture();
        ManagedAccountStatusResult before = status(false);
        ManagedAccountStatusResult after = status(true);
        when(fixture.remoteOperationGateway.getManagedAccountStatus(any(), anyString(), eq(DEVICE_ID)))
                .thenReturn(handle(RemoteOperationOutcome.success("status", before)))
                .thenReturn(handle(RemoteOperationOutcome.success("status", after)));
        when(fixture.credentialVaultService.list("vault-token")).thenReturn(List.of());
        when(fixture.credentialVaultService.add(eq("vault-token"), any(CredentialVaultEntryDraft.class)))
                .thenReturn(new CredentialVaultEntryMetadata(
                        "cred-1",
                        CredentialType.WINDOWS_ACCOUNT,
                        "Managed Windows account PC14\\Primaria",
                        "PC14\\Primaria",
                        Instant.parse("2026-09-14T12:00:00Z"),
                        Instant.parse("2026-09-14T12:00:00Z")));
        when(fixture.provisioningBridge.provision(
                eq("vault-token"),
                eq("cred-1"),
                eq(DEVICE_ID),
                anyString(),
                eq(ManagedWindowsAccountType.PRIMARY))).thenReturn(RemoteOperationOutcome.success("provisioned"));

        var response = fixture.service.configureCredential(
                CLASSROOM_ID,
                DEVICE_ID,
                "PRIMARY",
                "vault-token",
                "windows-secret");

        assertThat(response.provisioningStatus()).isEqualTo("SUCCESS");
        assertThat(response.errorCode()).isNull();
        assertThat(response.account().credentialConfigured()).isTrue();
        assertThat(response.account().credentialStatus()).isEqualTo("READY");

        InOrder order = inOrder(fixture.masterAccessGuard, fixture.credentialVaultService, fixture.provisioningBridge);
        order.verify(fixture.masterAccessGuard).requireAuthorized();
        order.verify(fixture.credentialVaultService).add(eq("vault-token"), any(CredentialVaultEntryDraft.class));
        order.verify(fixture.provisioningBridge).provision(
                eq("vault-token"),
                eq("cred-1"),
                eq(DEVICE_ID),
                anyString(),
                eq(ManagedWindowsAccountType.PRIMARY));
    }

    @Test
    void configureCredentialDoesNotProvisionWhenVaultWriteFails() {
        Fixture fixture = new Fixture();
        when(fixture.remoteOperationGateway.getManagedAccountStatus(any(), anyString(), eq(DEVICE_ID)))
                .thenReturn(handle(RemoteOperationOutcome.success("status", status(false))));
        when(fixture.credentialVaultService.list("vault-token")).thenReturn(List.of());
        when(fixture.credentialVaultService.add(eq("vault-token"), any(CredentialVaultEntryDraft.class)))
                .thenThrow(new com.galtek.classroom.credentialvault.CredentialVaultException(
                        ErrorCode.CREDENTIAL_VAULT_LOCKED,
                        "Credential vault is locked."));

        org.assertj.core.api.Assertions.assertThatThrownBy(() -> fixture.service.configureCredential(
                        CLASSROOM_ID,
                        DEVICE_ID,
                        "PRIMARY",
                        "vault-token",
                        "windows-secret"))
                .isInstanceOf(com.galtek.classroom.credentialvault.CredentialVaultException.class);

        verify(fixture.provisioningBridge, never()).provision(anyString(), anyString(), anyString(), anyString(), any());
    }

    @Test
    void bindDispatchesTypedOperationAndRefreshesOnlyAfterSuccess() {
        Fixture fixture = new Fixture();
        when(fixture.remoteOperationGateway.setManagedAccountBinding(
                any(), anyString(), eq(DEVICE_ID),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY), eq("PC14\\Primaria")))
                .thenReturn(handle(RemoteOperationOutcome.success("bound")));
        when(fixture.remoteOperationGateway.getManagedAccountStatus(any(), anyString(), eq(DEVICE_ID)))
                .thenReturn(handle(RemoteOperationOutcome.success("status", status(false))));

        var response = fixture.service.bind(CLASSROOM_ID, DEVICE_ID, "PRIMARY", "PC14\\Primaria");

        assertThat(response.status()).isEqualTo("SUCCESS");
        assertThat(response.account()).isNotNull();
        assertThat(response.account().accountId()).isEqualTo("PRIMARY");
        verify(fixture.remoteOperationGateway).setManagedAccountBinding(
                any(), eq(response.operationId()), eq(DEVICE_ID),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY), eq("PC14\\Primaria"));
    }

    @Test
    void bindReturnsTypedConflictWithoutRefreshingStatus() {
        Fixture fixture = new Fixture();
        when(fixture.remoteOperationGateway.setManagedAccountBinding(
                any(), anyString(), eq(DEVICE_ID), any(), anyString()))
                .thenReturn(handle(RemoteOperationOutcome.failed(
                        ErrorCode.MANAGED_ACCOUNT_BINDING_CONFLICT, "conflict")));

        var response = fixture.service.bind(CLASSROOM_ID, DEVICE_ID, "SECONDARY", "PC14\\Secundaria");

        assertThat(response.status()).isEqualTo("FAILED");
        assertThat(response.errorCode()).isEqualTo("MANAGED_ACCOUNT_BINDING_CONFLICT");
        assertThat(response.account()).isNull();
        verify(fixture.remoteOperationGateway, never()).getManagedAccountStatus(any(), anyString(), anyString());
    }

    @Test
    void bindRejectsMissingBindingCapabilityBeforeDispatch() {
        Fixture fixture = new Fixture();
        when(fixture.connectionRegistry.findByDeviceId(DEVICE_ID)).thenReturn(Optional.of(
                fixture.snapshot(EnumSet.of(DeviceCapability.MANAGED_ACCOUNT_STATUS_V1))));

        org.assertj.core.api.Assertions.assertThatThrownBy(() -> fixture.service.bind(
                        CLASSROOM_ID, DEVICE_ID, "ADMIN", "ADMIN-14"))
                .isInstanceOf(com.galtek.classroom.api.ApiException.class)
                .hasMessageContaining("MANAGED_ACCOUNT_BINDING_V2");

        verify(fixture.remoteOperationGateway, never())
                .setManagedAccountBinding(any(), anyString(), anyString(), any(), anyString());
    }

    @Test
    void credentialProvisioningFailureKeepsObservedMissingState() {
        Fixture fixture = new Fixture();
        when(fixture.remoteOperationGateway.getManagedAccountStatus(any(), anyString(), eq(DEVICE_ID)))
                .thenReturn(handle(RemoteOperationOutcome.success("status", status(false))));
        when(fixture.credentialVaultService.list("vault-token")).thenReturn(List.of());
        when(fixture.credentialVaultService.add(eq("vault-token"), any(CredentialVaultEntryDraft.class)))
                .thenReturn(new CredentialVaultEntryMetadata(
                        "cred-1", CredentialType.WINDOWS_ACCOUNT, "managed", "PC14\\Primaria",
                        Instant.parse("2026-09-14T12:00:00Z"), Instant.parse("2026-09-14T12:00:00Z")));
        when(fixture.provisioningBridge.provision(
                eq("vault-token"), eq("cred-1"), eq(DEVICE_ID), anyString(),
                eq(ManagedWindowsAccountType.PRIMARY)))
                .thenReturn(RemoteOperationOutcome.failed(
                        ErrorCode.CREDENTIAL_NOT_PROVISIONABLE, "provision failed"));

        var response = fixture.service.configureCredential(
                CLASSROOM_ID, DEVICE_ID, "PRIMARY", "vault-token", "windows-secret");

        assertThat(response.provisioningStatus()).isEqualTo("FAILED");
        assertThat(response.account().configured()).isTrue();
        assertThat(response.account().credentialConfigured()).isFalse();
        assertThat(response.account().credentialStatus()).isEqualTo("CREDENTIAL_NOT_CONFIGURED");
        verify(fixture.remoteOperationGateway).getManagedAccountStatus(any(), anyString(), eq(DEVICE_ID));
    }

    private static ManagedAccountStatusResult status(boolean ready) {
        return ManagedAccountStatusResult.newBuilder()
                .addAccounts(ManagedAccountStatus.newBuilder()
                        .setAccountId(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY)
                        .setConfigured(true)
                        .setCredentialConfigured(ready)
                        .setCredentialStatus(ready
                                ? ManagedAccountCredentialStatus.MANAGED_ACCOUNT_CREDENTIAL_STATUS_READY
                                : ManagedAccountCredentialStatus
                                        .MANAGED_ACCOUNT_CREDENTIAL_STATUS_CREDENTIAL_NOT_CONFIGURED)
                        .setWindowsAccountName("PC14\\Primaria"))
                .addAccounts(ManagedAccountStatus.newBuilder()
                        .setAccountId(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY)
                        .setConfigured(false)
                        .setCredentialStatus(ManagedAccountCredentialStatus
                                .MANAGED_ACCOUNT_CREDENTIAL_STATUS_NOT_CONFIGURED))
                .addAccounts(ManagedAccountStatus.newBuilder()
                        .setAccountId(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_ADMIN)
                        .setConfigured(false)
                        .setCredentialStatus(ManagedAccountCredentialStatus
                                .MANAGED_ACCOUNT_CREDENTIAL_STATUS_NOT_CONFIGURED))
                .build();
    }

    private static Optional<DispatchHandle> handle(RemoteOperationOutcome outcome) {
        return Optional.of(new DispatchHandle(
                new RemoteOperationKey(DEVICE_ID, UUID.randomUUID().toString()),
                CompletableFuture.completedFuture(outcome)));
    }

    private static final class Fixture {
        final MasterAccessGuard masterAccessGuard = mock(MasterAccessGuard.class);
        final MasterStorageState storageState = mock(MasterStorageState.class);
        final MasterAdminRepository adminRepository = mock(MasterAdminRepository.class);
        final DeviceRepository deviceRepository = mock(DeviceRepository.class);
        final DeviceNetworkBindingRepository bindingRepository = mock(DeviceNetworkBindingRepository.class);
        final MasterPairingService pairingService = mock(MasterPairingService.class);
        final ClientConnectionRegistry connectionRegistry = mock(ClientConnectionRegistry.class);
        final MasterRemoteOperationGateway remoteOperationGateway = mock(MasterRemoteOperationGateway.class);
        final CredentialVaultService credentialVaultService = mock(CredentialVaultService.class);
        final ManagedCredentialProvisioningBridge provisioningBridge = mock(ManagedCredentialProvisioningBridge.class);
        final ManagedAccountAdminService service = new ManagedAccountAdminService(
                masterAccessGuard,
                storageState,
                adminRepository,
                deviceRepository,
                bindingRepository,
                pairingService,
                connectionRegistry,
                remoteOperationGateway,
                credentialVaultService,
                provisioningBridge);

        Fixture() {
            when(masterAccessGuard.requireAuthorized()).thenReturn(new MasterAuthorizationResponse(
                    "AUTHORIZED",
                    true,
                    true,
                    "AULA\\Maestra",
                    "AULA\\Maestra"));
            when(storageState.health()).thenReturn(MasterStorageHealth.ready());
            when(adminRepository.findClassroom(CLASSROOM_ID)).thenReturn(Optional.of(new ClassroomResponse(
                    CLASSROOM_ID,
                    "Aula",
                    true,
                    Set.of(),
                    null,
                    true,
                    true,
                    0,
                    new ClassroomCounts(0, 0, 0, 1, 0, 0))));
            when(deviceRepository.findByClassroomId(CLASSROOM_ID)).thenReturn(List.of(new Device(
                    DEVICE_ID,
                    INSTALLATION_ID.toString(),
                    "PC14",
                    "PC14",
                    DeviceStatus.ONLINE,
                    OffsetDateTime.ofInstant(Instant.parse("2026-09-14T12:00:00Z"), ZoneOffset.UTC),
                    Set.of(),
                    null)));
            when(bindingRepository.findCurrentByDeviceIds(List.of(DEVICE_ID))).thenReturn(List.of(new RegisteredNetworkDevice(
                    "binding-1",
                    DEVICE_ID,
                    CLASSROOM_ID,
                    INSTALLATION_ID,
                    NETWORK_IDENTITY_ID,
                    FINGERPRINT,
                    "PC14",
                    "PC14",
                    "0.5.0",
                    Set.of(),
                    OffsetDateTime.now(ZoneOffset.UTC),
                    OffsetDateTime.now(ZoneOffset.UTC),
                    true,
                    0)));
            when(pairingService.knownClients()).thenReturn(List.of(new KnownMasterClient(
                    PairingStatus.PAIRED,
                    NETWORK_IDENTITY_ID,
                    INSTALLATION_ID,
                    FINGERPRINT,
                    "spki",
                    Instant.parse("2026-09-14T12:00:00Z"),
                    null)));
            when(connectionRegistry.findByDeviceId(DEVICE_ID)).thenReturn(Optional.of(snapshot(EnumSet.of(
                    DeviceCapability.MANAGED_ACCOUNT_STATUS_V1,
                    DeviceCapability.MANAGED_CREDENTIAL_PROVISIONING_V1,
                    DeviceCapability.MANAGED_ACCOUNT_BINDING_V2))));
            when(remoteOperationGateway.resultTimeout()).thenReturn(java.time.Duration.ofMillis(100));
        }

        ClientConnectionSnapshot snapshot(EnumSet<DeviceCapability> capabilities) {
            return new ClientConnectionSnapshot(
                    NETWORK_IDENTITY_ID,
                    INSTALLATION_ID,
                    DEVICE_ID,
                    CLASSROOM_ID,
                    true,
                    "PC14",
                    "PC14",
                    DeviceStatus.ONLINE,
                    "0.5.0",
                    capabilities,
                    Instant.parse("2026-09-14T12:00:00Z"),
                    Instant.parse("2026-09-14T12:00:00Z"),
                    null,
                    "connection-1",
                    null);
        }
    }
}
