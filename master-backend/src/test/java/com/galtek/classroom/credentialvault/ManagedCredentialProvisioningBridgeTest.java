package com.galtek.classroom.credentialvault;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;

import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.master.MasterAccessDeniedException;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.master.MasterUnlockAccessGuard;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientConnectionSnapshot;
import com.galtek.classroom.network.MasterRemoteOperationGateway;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationKey;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.v1.ManagedWindowsAccountId;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.windows.ManagedWindowsAccountType;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;

class ManagedCredentialProvisioningBridgeTest {

    private static final String TOKEN = "vault-session-token";
    private static final String CREDENTIAL_ID = "cred_windows";
    private static final String DEVICE_ID = "PC01";
    private static final String OPERATION_ID = "operation-1";
    private static final Instant NOW = Instant.parse("2026-09-04T12:00:00Z");

    @Test
    void authorizedMasterWithValidWindowsCredentialDispatchesProvisioning() {
        BridgeFixture fixture = newBridge();
        ClientConnectionSnapshot snapshot = snapshot(DEVICE_ID, true, DeviceStatus.ONLINE,
                DeviceCapability.MANAGED_CREDENTIAL_PROVISIONING_V1);
        RemoteOperationOutcome success = RemoteOperationOutcome.success("ok");
        when(fixture.connectionRegistry().findByDeviceId(DEVICE_ID)).thenReturn(Optional.of(snapshot));
        when(fixture.credentialVaultService().list(TOKEN)).thenReturn(List.of(metadata(CredentialType.WINDOWS_ACCOUNT)));
        when(fixture.credentialVaultService().readInternal(TOKEN, CREDENTIAL_ID))
                .thenReturn(entry(CredentialType.WINDOWS_ACCOUNT, "Clase 1!"));
        when(fixture.remoteOperationGateway().provisionManagedCredential(
                eq(snapshot),
                eq(OPERATION_ID),
                eq(DEVICE_ID),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY),
                any(byte[].class))).thenReturn(Optional.of(handle(success)));

        RemoteOperationOutcome result = fixture.bridge().provision(
                TOKEN,
                CREDENTIAL_ID,
                DEVICE_ID,
                OPERATION_ID,
                ManagedWindowsAccountType.PRIMARY);

        assertThat(result).isSameAs(success);
        verify(fixture.masterAccessGuard()).requireAuthorized();
        verify(fixture.credentialVaultService()).list(TOKEN);
        verify(fixture.credentialVaultService()).readInternal(TOKEN, CREDENTIAL_ID);
        verify(fixture.remoteOperationGateway()).provisionManagedCredential(
                eq(snapshot),
                eq(OPERATION_ID),
                eq(DEVICE_ID),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY),
                any(byte[].class));
    }

    @Test
    void rejectedMasterDoesNotTouchVaultOrGateway() {
        BridgeFixture fixture = newBridge();
        doThrow(new MasterAccessDeniedException("CURRENT_ACCOUNT_NOT_AUTHORIZED"))
                .when(fixture.masterAccessGuard()).requireAuthorized();

        assertThatThrownBy(() -> fixture.bridge().provision(
                TOKEN,
                CREDENTIAL_ID,
                DEVICE_ID,
                OPERATION_ID,
                ManagedWindowsAccountType.PRIMARY))
                .isInstanceOf(MasterAccessDeniedException.class);

        verifyNoInteractions(fixture.credentialVaultService());
        verifyNoInteractions(fixture.connectionRegistry());
        verify(fixture.remoteOperationGateway(), never()).provisionManagedCredential(
                any(), any(), any(), any(), any());
    }

    @Test
    void masterUnlockAccessGuardIsNotPartOfTheBridge() {
        assertThat(ManagedCredentialProvisioningBridge.class.getDeclaredFields())
                .extracting(field -> field.getType().getName())
                .doesNotContain(MasterUnlockAccessGuard.class.getName());
    }

    @Test
    void lockedVaultStopsBeforeGateway() {
        BridgeFixture fixture = newBridge();
        when(fixture.credentialVaultService().list(TOKEN))
                .thenThrow(new CredentialVaultException(ErrorCode.CREDENTIAL_VAULT_LOCKED, "Credential vault is locked."));

        assertVaultError(
                () -> fixture.bridge().provision(
                        TOKEN,
                        CREDENTIAL_ID,
                        DEVICE_ID,
                        OPERATION_ID,
                        ManagedWindowsAccountType.PRIMARY),
                ErrorCode.CREDENTIAL_VAULT_LOCKED);

        verifyNoInteractions(fixture.connectionRegistry());
        verify(fixture.remoteOperationGateway(), never()).provisionManagedCredential(any(), any(), any(), any(), any());
    }

    @Test
    void expiredVaultSessionStopsBeforeGateway() {
        BridgeFixture fixture = newBridge();
        when(fixture.credentialVaultService().list(TOKEN))
                .thenThrow(new CredentialVaultException(ErrorCode.CREDENTIAL_VAULT_LOCKED, "Credential vault is locked."));

        assertVaultError(
                () -> fixture.bridge().provision(
                        TOKEN,
                        CREDENTIAL_ID,
                        DEVICE_ID,
                        OPERATION_ID,
                        ManagedWindowsAccountType.SECONDARY),
                ErrorCode.CREDENTIAL_VAULT_LOCKED);

        verify(fixture.remoteOperationGateway(), never()).provisionManagedCredential(any(), any(), any(), any(), any());
    }

    @Test
    void missingCredentialStopsBeforeGateway() {
        BridgeFixture fixture = newBridge();
        when(fixture.credentialVaultService().list(TOKEN)).thenReturn(List.of());

        assertVaultError(
                () -> fixture.bridge().provision(
                        TOKEN,
                        CREDENTIAL_ID,
                        DEVICE_ID,
                        OPERATION_ID,
                        ManagedWindowsAccountType.PRIMARY),
                ErrorCode.CREDENTIAL_NOT_FOUND);

        verify(fixture.credentialVaultService(), never()).readInternal(any(), any());
        verify(fixture.remoteOperationGateway(), never()).provisionManagedCredential(any(), any(), any(), any(), any());
    }

    @Test
    void googleCredentialIsRejectedBeforeSecretReadOrGateway() {
        BridgeFixture fixture = newBridge();
        String googlePassword = "google-secret";
        when(fixture.credentialVaultService().list(TOKEN)).thenReturn(List.of(metadata(CredentialType.GOOGLE_ACCOUNT)));

        assertThatThrownBy(() -> fixture.bridge().provision(
                TOKEN,
                CREDENTIAL_ID,
                DEVICE_ID,
                OPERATION_ID,
                ManagedWindowsAccountType.PRIMARY))
                .isInstanceOf(CredentialVaultException.class)
                .satisfies(throwable -> assertThat(((CredentialVaultException) throwable).errorCode())
                        .isEqualTo(ErrorCode.CREDENTIAL_NOT_PROVISIONABLE))
                .hasMessageNotContaining(googlePassword);

        verify(fixture.credentialVaultService(), never()).readInternal(any(), any());
        verify(fixture.remoteOperationGateway(), never()).provisionManagedCredential(any(), any(), any(), any(), any());
    }

    @Test
    void offlineDeviceDoesNotExtractPasswordOrCallGateway() {
        BridgeFixture fixture = newBridge();
        when(fixture.credentialVaultService().list(TOKEN)).thenReturn(List.of(metadata(CredentialType.WINDOWS_ACCOUNT)));
        when(fixture.connectionRegistry().findByDeviceId(DEVICE_ID)).thenReturn(Optional.empty());

        RemoteOperationOutcome result = fixture.bridge().provision(
                TOKEN,
                CREDENTIAL_ID,
                DEVICE_ID,
                OPERATION_ID,
                ManagedWindowsAccountType.PRIMARY);

        assertThat(result.status()).isEqualTo(TargetExecutionStatus.FAILED);
        assertThat(result.errorCode()).isEqualTo(ErrorCode.DEVICE_OFFLINE);
        verify(fixture.credentialVaultService(), never()).readInternal(any(), any());
        verify(fixture.remoteOperationGateway(), never()).provisionManagedCredential(any(), any(), any(), any(), any());
    }

    @Test
    void missingProvisioningCapabilityDoesNotExtractPasswordOrCallGateway() {
        BridgeFixture fixture = newBridge();
        when(fixture.credentialVaultService().list(TOKEN)).thenReturn(List.of(metadata(CredentialType.WINDOWS_ACCOUNT)));
        when(fixture.connectionRegistry().findByDeviceId(DEVICE_ID))
                .thenReturn(Optional.of(snapshot(DEVICE_ID, true, DeviceStatus.ONLINE)));

        RemoteOperationOutcome result = fixture.bridge().provision(
                TOKEN,
                CREDENTIAL_ID,
                DEVICE_ID,
                OPERATION_ID,
                ManagedWindowsAccountType.PRIMARY);

        assertThat(result.status()).isEqualTo(TargetExecutionStatus.FAILED);
        assertThat(result.errorCode()).isEqualTo(ErrorCode.CAPABILITY_NOT_SUPPORTED);
        verify(fixture.credentialVaultService(), never()).readInternal(any(), any());
        verify(fixture.remoteOperationGateway(), never()).provisionManagedCredential(any(), any(), any(), any(), any());
    }

    @Test
    void passwordIsEncodedAsExactUtf16LittleEndianWithoutBomOrNullTerminator() {
        String secret = "  p\u00e1ss \u03a9 \uD83D\uDD10  ";
        BridgeFixture fixture = newBridge();
        ClientConnectionSnapshot snapshot = snapshot(DEVICE_ID, true, DeviceStatus.ONLINE,
                DeviceCapability.MANAGED_CREDENTIAL_PROVISIONING_V1);
        byte[][] sentPassword = new byte[1][];
        when(fixture.connectionRegistry().findByDeviceId(DEVICE_ID)).thenReturn(Optional.of(snapshot));
        when(fixture.credentialVaultService().list(TOKEN)).thenReturn(List.of(metadata(CredentialType.WINDOWS_ACCOUNT)));
        when(fixture.credentialVaultService().readInternal(TOKEN, CREDENTIAL_ID))
                .thenReturn(entry(CredentialType.WINDOWS_ACCOUNT, secret));
        when(fixture.remoteOperationGateway().provisionManagedCredential(
                eq(snapshot),
                eq(OPERATION_ID),
                eq(DEVICE_ID),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY),
                any(byte[].class))).thenAnswer(invocation -> {
                    sentPassword[0] = invocation.getArgument(4, byte[].class).clone();
                    return Optional.of(handle(RemoteOperationOutcome.success("ok")));
                });

        fixture.bridge().provision(TOKEN, CREDENTIAL_ID, DEVICE_ID, OPERATION_ID, ManagedWindowsAccountType.PRIMARY);

        verify(fixture.remoteOperationGateway()).provisionManagedCredential(
                any(),
                eq(OPERATION_ID),
                eq(DEVICE_ID),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY),
                any(byte[].class));
        byte[] captured = sentPassword[0];
        byte[] expected = secret.getBytes(StandardCharsets.UTF_16LE);
        assertThat(captured).containsExactly(expected);
        assertThat(captured[0] == (byte) 0xFF && captured[1] == (byte) 0xFE).isFalse();
        assertThat(captured[captured.length - 2] == (byte) 0 && captured[captured.length - 1] == (byte) 0)
                .isFalse();
        assertThat(new String(captured, StandardCharsets.UTF_16LE)).isEqualTo(secret);
    }

    @Test
    void controlledPasswordBytesAreClearedAfterSuccess() {
        BridgeFixture fixture = successFixture("secret", ManagedWindowsAccountType.PRIMARY);
        ArgumentCaptor<byte[]> password = ArgumentCaptor.forClass(byte[].class);

        fixture.bridge().provision(TOKEN, CREDENTIAL_ID, DEVICE_ID, OPERATION_ID, ManagedWindowsAccountType.PRIMARY);

        verify(fixture.remoteOperationGateway()).provisionManagedCredential(
                any(), any(), any(), any(), password.capture());
        assertThat(password.getValue()).containsOnly((byte) 0);
    }

    @Test
    void controlledPasswordBytesAreClearedAfterStructuredFailure() {
        BridgeFixture fixture = configuredBridge(
                "secret",
                ManagedWindowsAccountType.PRIMARY,
                RemoteOperationOutcome.failed(
                        ErrorCode.MANAGED_CREDENTIAL_PROTECTION_FAILED,
                        "Managed Windows credential protection failed on the target device."));
        ArgumentCaptor<byte[]> password = ArgumentCaptor.forClass(byte[].class);

        RemoteOperationOutcome result = fixture.bridge().provision(
                TOKEN,
                CREDENTIAL_ID,
                DEVICE_ID,
                OPERATION_ID,
                ManagedWindowsAccountType.PRIMARY);

        assertThat(result.errorCode()).isEqualTo(ErrorCode.MANAGED_CREDENTIAL_PROTECTION_FAILED);
        verify(fixture.remoteOperationGateway()).provisionManagedCredential(
                any(), any(), any(), any(), password.capture());
        assertThat(password.getValue()).containsOnly((byte) 0);
    }

    @Test
    void controlledPasswordBytesAreClearedAfterTimeoutAndNoRetryOccurs() {
        BridgeFixture fixture = newBridge();
        ClientConnectionSnapshot snapshot = snapshot(DEVICE_ID, true, DeviceStatus.ONLINE,
                DeviceCapability.MANAGED_CREDENTIAL_PROVISIONING_V1);
        CompletableFuture<RemoteOperationOutcome> pending = new CompletableFuture<>();
        DispatchHandle handle = new DispatchHandle(new RemoteOperationKey(DEVICE_ID, OPERATION_ID), pending);
        RemoteOperationOutcome unknown = RemoteOperationOutcome.unknown("Operation result is unknown after timeout.");
        when(fixture.connectionRegistry().findByDeviceId(DEVICE_ID)).thenReturn(Optional.of(snapshot));
        when(fixture.credentialVaultService().list(TOKEN)).thenReturn(List.of(metadata(CredentialType.WINDOWS_ACCOUNT)));
        when(fixture.credentialVaultService().readInternal(TOKEN, CREDENTIAL_ID))
                .thenReturn(entry(CredentialType.WINDOWS_ACCOUNT, "secret"));
        when(fixture.remoteOperationGateway().provisionManagedCredential(
                eq(snapshot),
                eq(OPERATION_ID),
                eq(DEVICE_ID),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY),
                any(byte[].class))).thenReturn(Optional.of(handle));
        when(fixture.remoteOperationGateway().timeout(handle)).thenReturn(unknown);
        ArgumentCaptor<byte[]> password = ArgumentCaptor.forClass(byte[].class);

        RemoteOperationOutcome result = fixture.bridge().provision(
                TOKEN,
                CREDENTIAL_ID,
                DEVICE_ID,
                OPERATION_ID,
                ManagedWindowsAccountType.PRIMARY);

        assertThat(result).isSameAs(unknown);
        verify(fixture.remoteOperationGateway(), times(1)).provisionManagedCredential(
                any(), any(), any(), any(), password.capture());
        verify(fixture.remoteOperationGateway(), times(1)).timeout(handle);
        assertThat(password.getValue()).containsOnly((byte) 0);
    }

    @Test
    void controlledPasswordBytesAreClearedAfterGatewayFailureWithoutSecretInException() {
        BridgeFixture fixture = newBridge();
        ClientConnectionSnapshot snapshot = snapshot(DEVICE_ID, true, DeviceStatus.ONLINE,
                DeviceCapability.MANAGED_CREDENTIAL_PROVISIONING_V1);
        when(fixture.connectionRegistry().findByDeviceId(DEVICE_ID)).thenReturn(Optional.of(snapshot));
        when(fixture.credentialVaultService().list(TOKEN)).thenReturn(List.of(metadata(CredentialType.WINDOWS_ACCOUNT)));
        when(fixture.credentialVaultService().readInternal(TOKEN, CREDENTIAL_ID))
                .thenReturn(entry(CredentialType.WINDOWS_ACCOUNT, "secret"));
        doThrow(new IllegalStateException("Dispatch failed."))
                .when(fixture.remoteOperationGateway())
                .provisionManagedCredential(
                        eq(snapshot),
                        eq(OPERATION_ID),
                        eq(DEVICE_ID),
                        eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY),
                        any(byte[].class));
        ArgumentCaptor<byte[]> password = ArgumentCaptor.forClass(byte[].class);

        assertThatThrownBy(() -> fixture.bridge().provision(
                TOKEN,
                CREDENTIAL_ID,
                DEVICE_ID,
                OPERATION_ID,
                ManagedWindowsAccountType.PRIMARY))
                .isInstanceOf(IllegalStateException.class)
                .hasMessageNotContaining("secret");

        verify(fixture.remoteOperationGateway()).provisionManagedCredential(
                any(), any(), any(), any(), password.capture());
        assertThat(password.getValue()).containsOnly((byte) 0);
    }

    @Test
    void secondaryAccountIsMappedWithoutSendingCredentialIdOrVaultToken() {
        BridgeFixture fixture = successFixture("secret", ManagedWindowsAccountType.SECONDARY);
        ArgumentCaptor<byte[]> password = ArgumentCaptor.forClass(byte[].class);

        fixture.bridge().provision(TOKEN, CREDENTIAL_ID, DEVICE_ID, OPERATION_ID, ManagedWindowsAccountType.SECONDARY);

        verify(fixture.remoteOperationGateway()).provisionManagedCredential(
                any(),
                eq(OPERATION_ID),
                eq(DEVICE_ID),
                eq(ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY),
                password.capture());
        assertThat(password.getValue()).containsOnly((byte) 0);
    }

    @Test
    void operationOutcomesArePropagatedWithoutConversionToString() {
        RemoteOperationOutcome success = RemoteOperationOutcome.success("Agent reported operation success.");
        RemoteOperationOutcome structuredFailure = RemoteOperationOutcome.failed(
                ErrorCode.ACCOUNT_NOT_CONFIGURED,
                "Managed Windows account is not configured on the target device.");
        RemoteOperationOutcome unknown = RemoteOperationOutcome.unknown("Operation result is unknown after disconnect.");

        assertThat(configuredBridge("secret", ManagedWindowsAccountType.PRIMARY, success)
                .bridge()
                .provision(TOKEN, CREDENTIAL_ID, DEVICE_ID, OPERATION_ID, ManagedWindowsAccountType.PRIMARY))
                .isSameAs(success);
        assertThat(configuredBridge("secret", ManagedWindowsAccountType.PRIMARY, structuredFailure)
                .bridge()
                .provision(TOKEN, CREDENTIAL_ID, DEVICE_ID, OPERATION_ID, ManagedWindowsAccountType.PRIMARY))
                .isSameAs(structuredFailure);
        assertThat(configuredBridge("secret", ManagedWindowsAccountType.PRIMARY, unknown)
                .bridge()
                .provision(TOKEN, CREDENTIAL_ID, DEVICE_ID, OPERATION_ID, ManagedWindowsAccountType.PRIMARY))
                .isSameAs(unknown);
    }

    @Test
    void bridgeHasNoLoggerFieldForSecretLogging() {
        assertThat(ManagedCredentialProvisioningBridge.class.getDeclaredFields())
                .extracting(field -> field.getType().getName())
                .noneMatch(typeName -> typeName.contains("Logger"));
    }

    private BridgeFixture successFixture(String password, ManagedWindowsAccountType accountType) {
        return configuredBridge(password, accountType, RemoteOperationOutcome.success("ok"));
    }

    private BridgeFixture configuredBridge(
            String password,
            ManagedWindowsAccountType accountType,
            RemoteOperationOutcome outcome) {
        BridgeFixture fixture = newBridge();
        ClientConnectionSnapshot snapshot = snapshot(DEVICE_ID, true, DeviceStatus.ONLINE,
                DeviceCapability.MANAGED_CREDENTIAL_PROVISIONING_V1);
        when(fixture.connectionRegistry().findByDeviceId(DEVICE_ID)).thenReturn(Optional.of(snapshot));
        when(fixture.credentialVaultService().list(TOKEN)).thenReturn(List.of(metadata(CredentialType.WINDOWS_ACCOUNT)));
        when(fixture.credentialVaultService().readInternal(TOKEN, CREDENTIAL_ID))
                .thenReturn(entry(CredentialType.WINDOWS_ACCOUNT, password));
        when(fixture.remoteOperationGateway().provisionManagedCredential(
                eq(snapshot),
                eq(OPERATION_ID),
                eq(DEVICE_ID),
                eq(toNetwork(accountType)),
                any(byte[].class))).thenReturn(Optional.of(handle(outcome)));
        return fixture;
    }

    private static BridgeFixture newBridge() {
        MasterAccessGuard masterAccessGuard = mock(MasterAccessGuard.class);
        CredentialVaultService credentialVaultService = mock(CredentialVaultService.class);
        ClientConnectionRegistry connectionRegistry = mock(ClientConnectionRegistry.class);
        MasterRemoteOperationGateway remoteOperationGateway = mock(MasterRemoteOperationGateway.class);
        when(remoteOperationGateway.resultTimeout()).thenReturn(Duration.ofNanos(1));
        return new BridgeFixture(
                new ManagedCredentialProvisioningBridge(
                        masterAccessGuard,
                        credentialVaultService,
                        connectionRegistry,
                        remoteOperationGateway),
                masterAccessGuard,
                credentialVaultService,
                connectionRegistry,
                remoteOperationGateway);
    }

    private static CredentialVaultEntryMetadata metadata(CredentialType credentialType) {
        return new CredentialVaultEntryMetadata(
                CREDENTIAL_ID,
                credentialType,
                "Visible",
                "login",
                NOW,
                NOW);
    }

    private static CredentialVaultEntry entry(CredentialType credentialType, String password) {
        return new CredentialVaultEntry(
                CREDENTIAL_ID,
                credentialType,
                "Visible",
                "login",
                password,
                NOW,
                NOW);
    }

    private static DispatchHandle handle(RemoteOperationOutcome outcome) {
        return new DispatchHandle(
                new RemoteOperationKey(DEVICE_ID, OPERATION_ID),
                CompletableFuture.completedFuture(outcome));
    }

    private static ClientConnectionSnapshot snapshot(
            String deviceId,
            boolean registered,
            DeviceStatus status,
            DeviceCapability... capabilities) {
        return new ClientConnectionSnapshot(
                UUID.randomUUID(),
                UUID.randomUUID(),
                deviceId,
                "classroom-1",
                registered,
                deviceId,
                deviceId.toLowerCase(),
                status,
                "0.5.0-test",
                Set.of(capabilities),
                NOW,
                NOW,
                null,
                "connection-1",
                null);
    }

    private static ManagedWindowsAccountId toNetwork(ManagedWindowsAccountType accountType) {
        return accountType == ManagedWindowsAccountType.PRIMARY
                ? ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_PRIMARY
                : ManagedWindowsAccountId.MANAGED_WINDOWS_ACCOUNT_ID_SECONDARY;
    }

    private static void assertVaultError(Runnable runnable, ErrorCode errorCode) {
        assertThatThrownBy(runnable::run)
                .isInstanceOf(CredentialVaultException.class)
                .satisfies(throwable -> assertThat(((CredentialVaultException) throwable).errorCode())
                        .isEqualTo(errorCode));
    }

    private record BridgeFixture(
            ManagedCredentialProvisioningBridge bridge,
            MasterAccessGuard masterAccessGuard,
            CredentialVaultService credentialVaultService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway remoteOperationGateway) {
    }
}
