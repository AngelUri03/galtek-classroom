package com.galtek.classroom.windows;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.activity.DeviceActivityRecorder;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientConnectionSnapshot;
import com.galtek.classroom.network.MasterRemoteOperationGateway;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationKey;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
import com.galtek.classroom.operations.BatchOperationService;
import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.OperationPayload;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.windows.WindowsSessionStateDtos.WindowsSessionStateBatchResponse;
import com.galtek.classroom.windows.WindowsSessionStateDtos.WindowsSessionStateTargetResponse;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;

class WindowsSessionLogoffServiceTest {
    private WindowsSessionStateReadService stateReadService;
    private ClientConnectionRegistry connectionRegistry;
    private MasterRemoteOperationGateway gateway;
    private BatchOperationService batchOperationService;
    private MasterAccessGuard accessGuard;
    private DeviceActivityRecorder activityRecorder;
    private WindowsSessionLogoffService service;

    @BeforeEach
    void setUp() {
        stateReadService = mock(WindowsSessionStateReadService.class);
        connectionRegistry = mock(ClientConnectionRegistry.class);
        gateway = mock(MasterRemoteOperationGateway.class);
        batchOperationService = mock(BatchOperationService.class);
        accessGuard = mock(MasterAccessGuard.class);
        activityRecorder = mock(DeviceActivityRecorder.class);
        when(accessGuard.requireAuthorized()).thenReturn(new MasterAuthorizationResponse(
                "AUTHORIZED", true, true, "AULA\\Maestra", "AULA\\Maestra"));
        service = new WindowsSessionLogoffService(
                stateReadService,
                connectionRegistry,
                gateway,
                batchOperationService,
                Clock.fixed(Instant.parse("2026-09-28T12:00:00Z"), ZoneOffset.UTC),
                accessGuard,
                activityRecorder);
    }

    @Test
    void noSessionIsIdempotentAndDoesNotDispatch() {
        when(stateReadService.read(eq("ROOM-1"), eq(Map.of("targetDeviceIds", List.of("DEV-1")))))
                .thenReturn(states("DEV-1", WindowsSessionState.NO_SESSION));

        var response = service.logoff("ROOM-1", Map.of(
                "accountId", "ADMIN",
                "targetDeviceIds", List.of("DEV-1")));

        assertThat(response.status()).isEqualTo("SUCCESS");
        assertThat(response.targets().getFirst().status()).isEqualTo("NO_CHANGE");
        verify(gateway, never()).logoffWindowsSession(
                org.mockito.ArgumentMatchers.any(), anyString(), anyString(),
                org.mockito.ArgumentMatchers.any());
        verify(batchOperationService).replaceResults(org.mockito.ArgumentMatchers.any());
    }

    @Test
    void adminActiveDispatchesTypedAdminLogoff() {
        when(stateReadService.read(eq("ROOM-1"), eq(Map.of("targetDeviceIds", List.of("DEV-1")))))
                .thenReturn(states("DEV-1", WindowsSessionState.ADMIN_ACTIVE));
        ClientConnectionSnapshot snapshot = mock(ClientConnectionSnapshot.class);
        when(snapshot.capabilities()).thenReturn(Set.of(DeviceCapability.WINDOWS_SESSION_LOGOFF_V1));
        when(connectionRegistry.findByDeviceId("DEV-1")).thenReturn(Optional.of(snapshot));
        when(gateway.resultTimeout()).thenReturn(Duration.ofSeconds(1));
        DispatchHandle handle = new DispatchHandle(
                new RemoteOperationKey("DEV-1", "OP-1"),
                CompletableFuture.completedFuture(RemoteOperationOutcome.success("accepted")));
        when(gateway.logoffWindowsSession(eq(snapshot), anyString(), eq("DEV-1"),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_ADMIN)))
                .thenReturn(Optional.of(handle));

        var response = service.logoff("ROOM-1", Map.of(
                "accountId", "ADMIN",
                "targetDeviceIds", List.of("DEV-1")));

        assertThat(response.status()).isEqualTo("SUCCESS");
        assertThat(response.targets().getFirst().status()).isEqualTo("SUCCESS");
        verify(gateway).logoffWindowsSession(eq(snapshot), anyString(), eq("DEV-1"),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_ADMIN));

        ArgumentCaptor<BatchOperation> intent = ArgumentCaptor.forClass(BatchOperation.class);
        ArgumentCaptor<OperationPayload> payload = ArgumentCaptor.forClass(OperationPayload.class);
        verify(batchOperationService).create(eq("ROOM-1"), intent.capture(), payload.capture());
        assertThat(intent.getValue().type()).isEqualTo(OperationType.LOGOFF_WINDOWS_SESSION);
        assertThat(intent.getValue().targets().getFirst().status()).isEqualTo(TargetExecutionStatus.PENDING);
        assertThat(payload.getValue().json()).isEqualTo("{\"schemaVersion\":1,\"accountId\":\"ADMIN\"}");

        ArgumentCaptor<BatchOperation> completed = ArgumentCaptor.forClass(BatchOperation.class);
        verify(batchOperationService).replaceResults(completed.capture());
        assertThat(completed.getValue().status().name()).isEqualTo("SUCCESS");
        assertThat(completed.getValue().targets().getFirst().status()).isEqualTo(TargetExecutionStatus.SUCCESS);
    }

    @Test
    void adminActiveCannotBeLoggedOffAsPrimary() {
        when(stateReadService.read(eq("ROOM-1"), eq(Map.of("targetDeviceIds", List.of("DEV-1")))))
                .thenReturn(states("DEV-1", WindowsSessionState.ADMIN_ACTIVE));

        var response = service.logoff("ROOM-1", Map.of(
                "accountId", "PRIMARY",
                "targetDeviceIds", List.of("DEV-1")));

        assertThat(response.status()).isEqualTo("FAILED");
        assertThat(response.targets().getFirst().errorCode()).isEqualTo("WINDOWS_SESSION_CHANGED");
    }

    private static WindowsSessionStateBatchResponse states(String deviceId, WindowsSessionState state) {
        return new WindowsSessionStateBatchResponse("ROOM-1", 1, List.of(
                new WindowsSessionStateTargetResponse(deviceId, state.name(), true, null, "observed")));
    }
}
