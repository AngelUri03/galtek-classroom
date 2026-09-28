package com.galtek.classroom.windows;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.galtek.classroom.admin.AdminDtos.ClassroomCounts;
import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.api.ApiException;
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
import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
import com.galtek.classroom.network.v1.WindowsAccountInventoryEntry;
import com.galtek.classroom.network.v1.WindowsAccountInventoryResult;
import com.galtek.classroom.operations.ErrorCode;
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

class WindowsAccountInventoryReadServiceTest {

    private static final String CLASSROOM_ID = "classroom-1";
    private static final String DEVICE_ID = "device-1";
    private static final UUID INSTALLATION_ID = UUID.fromString("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static final UUID NETWORK_IDENTITY_ID = UUID.fromString("11111111-2222-3333-4444-555555555555");
    private static final String FINGERPRINT = "fingerprint";

    @Test
    void readAuthorizesMapsResponseAndDoesNotPersistInventory() {
        Fixture fixture = new Fixture();
        WindowsAccountInventoryResult inventory = WindowsAccountInventoryResult.newBuilder()
                .addAccounts(WindowsAccountInventoryEntry.newBuilder()
                        .setAccountName("ADMIN-14")
                        .setDisplayName("Administración")
                        .setEnabled(true)
                        .setAdministrator(true)
                        .setManagedRole(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_UNSPECIFIED))
                .build();
        when(fixture.gateway.getWindowsAccountInventory(any(), anyString(), eq(DEVICE_ID)))
                .thenReturn(handle(RemoteOperationOutcome.success("inventory", inventory)));

        var response = fixture.service.read(CLASSROOM_ID, DEVICE_ID);

        assertThat(response.deviceId()).isEqualTo(DEVICE_ID);
        assertThat(response.accounts()).singleElement().satisfies(account -> {
            assertThat(account.accountName()).isEqualTo("ADMIN-14");
            assertThat(account.administrator()).isTrue();
            assertThat(account.managedRole()).isEqualTo("NONE");
        });
        verify(fixture.accessGuard).requireAuthorized();
        verify(fixture.gateway).getWindowsAccountInventory(any(), anyString(), eq(DEVICE_ID));
    }

    @Test
    void readRejectsDeviceOutsideClassroomBeforeDispatch() {
        Fixture fixture = new Fixture();
        when(fixture.deviceRepository.findByClassroomId(CLASSROOM_ID)).thenReturn(List.of());

        assertCode(fixture, ErrorCode.DEVICE_NOT_FOUND);
        verify(fixture.gateway, never()).getWindowsAccountInventory(any(), anyString(), anyString());
    }

    @Test
    void readRejectsOfflineAndMissingCapability() {
        Fixture offline = new Fixture();
        when(offline.connectionRegistry.findByDeviceId(DEVICE_ID)).thenReturn(Optional.empty());
        assertCode(offline, ErrorCode.DEVICE_OFFLINE);

        Fixture missingCapability = new Fixture();
        when(missingCapability.connectionRegistry.findByDeviceId(DEVICE_ID))
                .thenReturn(Optional.of(missingCapability.snapshot(Set.of())));
        assertCode(missingCapability, ErrorCode.CAPABILITY_NOT_SUPPORTED);
    }

    @Test
    void readRejectsUnpairedAndRevokedClients() {
        Fixture unpaired = new Fixture();
        when(unpaired.pairingService.knownClients()).thenReturn(List.of());
        assertCode(unpaired, ErrorCode.MASTER_NOT_PAIRED);

        Fixture revoked = new Fixture();
        when(revoked.pairingService.knownClients()).thenReturn(List.of(revoked.client(PairingStatus.REVOKED)));
        assertCode(revoked, ErrorCode.CLIENT_REVOKED);
    }

    private static void assertCode(Fixture fixture, ErrorCode code) {
        assertThatThrownBy(() -> fixture.service.read(CLASSROOM_ID, DEVICE_ID))
                .isInstanceOfSatisfying(ApiException.class,
                        exception -> assertThat(exception.code()).isEqualTo(code.name()));
    }

    private static Optional<DispatchHandle> handle(RemoteOperationOutcome outcome) {
        return Optional.of(new DispatchHandle(
                new RemoteOperationKey(DEVICE_ID, UUID.randomUUID().toString()),
                CompletableFuture.completedFuture(outcome)));
    }

    private static final class Fixture {
        final MasterAccessGuard accessGuard = mock(MasterAccessGuard.class);
        final MasterStorageState storageState = mock(MasterStorageState.class);
        final MasterAdminRepository adminRepository = mock(MasterAdminRepository.class);
        final DeviceRepository deviceRepository = mock(DeviceRepository.class);
        final DeviceNetworkBindingRepository bindingRepository = mock(DeviceNetworkBindingRepository.class);
        final MasterPairingService pairingService = mock(MasterPairingService.class);
        final ClientConnectionRegistry connectionRegistry = mock(ClientConnectionRegistry.class);
        final MasterRemoteOperationGateway gateway = mock(MasterRemoteOperationGateway.class);
        final WindowsAccountInventoryReadService service = new WindowsAccountInventoryReadService(
                accessGuard, storageState, adminRepository, deviceRepository, bindingRepository,
                pairingService, connectionRegistry, gateway);

        Fixture() {
            when(accessGuard.requireAuthorized()).thenReturn(new MasterAuthorizationResponse(
                    "AUTHORIZED", true, true, "AULA\\Maestra", "AULA\\Maestra"));
            when(storageState.health()).thenReturn(MasterStorageHealth.ready());
            when(adminRepository.findClassroom(CLASSROOM_ID)).thenReturn(Optional.of(new ClassroomResponse(
                    CLASSROOM_ID, "Aula", true, Set.of(), null, true, true, 0,
                    new ClassroomCounts(0, 0, 0, 1, 0, 0))));
            when(deviceRepository.findByClassroomId(CLASSROOM_ID)).thenReturn(List.of(new Device(
                    DEVICE_ID, INSTALLATION_ID.toString(), "PC14", "PC14", DeviceStatus.ONLINE,
                    OffsetDateTime.ofInstant(Instant.parse("2026-09-23T12:00:00Z"), ZoneOffset.UTC),
                    Set.of(), null)));
            when(bindingRepository.findCurrentByDeviceIds(List.of(DEVICE_ID))).thenReturn(List.of(binding()));
            when(pairingService.knownClients()).thenReturn(List.of(client(PairingStatus.PAIRED)));
            when(connectionRegistry.findByDeviceId(DEVICE_ID)).thenReturn(Optional.of(snapshot(
                    EnumSet.of(DeviceCapability.WINDOWS_ACCOUNT_INVENTORY_V1))));
            when(gateway.resultTimeout()).thenReturn(java.time.Duration.ofMillis(100));
        }

        RegisteredNetworkDevice binding() {
            return new RegisteredNetworkDevice("binding-1", DEVICE_ID, CLASSROOM_ID, INSTALLATION_ID,
                    NETWORK_IDENTITY_ID, FINGERPRINT, "PC14", "PC14", "0.5.0", Set.of(),
                    OffsetDateTime.now(ZoneOffset.UTC), OffsetDateTime.now(ZoneOffset.UTC), true, 0);
        }

        KnownMasterClient client(PairingStatus status) {
            return new KnownMasterClient(status, NETWORK_IDENTITY_ID, INSTALLATION_ID, FINGERPRINT,
                    "spki", Instant.parse("2026-09-23T12:00:00Z"), null);
        }

        ClientConnectionSnapshot snapshot(Set<DeviceCapability> capabilities) {
            return new ClientConnectionSnapshot(NETWORK_IDENTITY_ID, INSTALLATION_ID, DEVICE_ID,
                    CLASSROOM_ID, true, "PC14", "PC14", DeviceStatus.ONLINE, "0.5.0", capabilities,
                    Instant.parse("2026-09-23T12:00:00Z"), Instant.parse("2026-09-23T12:00:00Z"),
                    null, "connection-1", null);
        }
    }
}
