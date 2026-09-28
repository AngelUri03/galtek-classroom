package com.galtek.classroom.windows;

import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
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
import com.galtek.classroom.network.v1.WindowsAccountInventoryResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import com.galtek.classroom.windows.WindowsAccountInventoryDtos.WindowsAccountInventoryResponse;
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
public class WindowsAccountInventoryReadService {

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterAdminRepository adminRepository;
    private final DeviceRepository deviceRepository;
    private final DeviceNetworkBindingRepository bindingRepository;
    private final MasterPairingService pairingService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway remoteOperationGateway;

    public WindowsAccountInventoryReadService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterAdminRepository adminRepository,
            DeviceRepository deviceRepository,
            DeviceNetworkBindingRepository bindingRepository,
            MasterPairingService pairingService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway remoteOperationGateway) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.adminRepository = adminRepository;
        this.deviceRepository = deviceRepository;
        this.bindingRepository = bindingRepository;
        this.pairingService = pairingService;
        this.connectionRegistry = connectionRegistry;
        this.remoteOperationGateway = remoteOperationGateway;
    }

    public WindowsAccountInventoryResponse read(String classroomId, String deviceId) {
        requireAuthorizedAndStorage();
        TargetPlan target = preflight(classroomId, deviceId);
        String operationId = UUID.randomUUID().toString();
        RemoteOperationOutcome outcome = remoteOperationGateway.getWindowsAccountInventory(
                        target.snapshot(), operationId, target.deviceId())
                .map(handle -> awaitOutcome(handle))
                .orElseGet(() -> RemoteOperationOutcome.failed(ErrorCode.DEVICE_OFFLINE, "Device is offline."));
        WindowsAccountInventoryResult inventory = outcome.windowsAccountInventory();
        if (outcome.status() != TargetExecutionStatus.SUCCESS || inventory == null) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    outcome.errorCode() == null ? ErrorCode.OPERATION_RESULT_UNKNOWN : outcome.errorCode(),
                    outcome.message());
        }

        return WindowsAccountInventoryResponse.from(target.deviceId(), inventory);
    }

    private TargetPlan preflight(String classroomId, String deviceId) {
        String cleanClassroomId = required(classroomId, "classroomId");
        ClassroomResponse classroom = adminRepository.findClassroom(cleanClassroomId)
                .orElseThrow(() -> new ApiException(
                        HttpStatus.NOT_FOUND, ErrorCode.CLASSROOM_NOT_FOUND, "Classroom was not found."));
        Map<String, Device> devices = deviceRepository.findByClassroomId(classroom.classroomId()).stream()
                .collect(Collectors.toMap(Device::deviceId, Function.identity(), (left, right) -> left));
        Device device = devices.get(required(deviceId, "deviceId"));
        if (device == null) {
            throw new ApiException(
                    HttpStatus.NOT_FOUND, ErrorCode.DEVICE_NOT_FOUND, "Device does not belong to classroom.");
        }

        RegisteredNetworkDevice binding = bindingRepository.findCurrentByDeviceIds(List.of(device.deviceId())).stream()
                .findFirst()
                .orElseThrow(() -> new ApiException(
                        HttpStatus.BAD_REQUEST,
                        ErrorCode.DEVICE_NOT_REGISTERED,
                        "Device is not registered for network operations."));
        requirePaired(binding);

        ClientConnectionSnapshot snapshot = connectionRegistry.findByDeviceId(device.deviceId()).orElse(null);
        if (!authenticatedOnline(snapshot, binding)) {
            throw new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.DEVICE_OFFLINE, "Device is offline.");
        }
        if (!snapshot.capabilities().contains(DeviceCapability.WINDOWS_ACCOUNT_INVENTORY_V1)) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    "Device does not announce WINDOWS_ACCOUNT_INVENTORY_V1.");
        }
        return new TargetPlan(device.deviceId(), snapshot);
    }

    private void requirePaired(RegisteredNetworkDevice binding) {
        Map<UUID, KnownMasterClient> clients = pairingService.knownClients().stream()
                .collect(Collectors.toMap(KnownMasterClient::clientNetworkIdentityId,
                        Function.identity(), (left, right) -> left));
        KnownMasterClient client = clients.get(binding.networkIdentityId());
        if (client != null && client.status() == PairingStatus.REVOKED) {
            throw new ApiException(HttpStatus.FORBIDDEN, ErrorCode.CLIENT_REVOKED,
                    "Client pairing has been revoked.");
        }
        if (client == null || client.status() != PairingStatus.PAIRED
                || !client.clientInstallationId().equals(binding.installationId())
                || !client.clientPublicKeyFingerprint().equals(binding.publicKeyFingerprint())) {
            throw new ApiException(HttpStatus.FORBIDDEN, ErrorCode.MASTER_NOT_PAIRED,
                    "Client is not paired with this Master.");
        }
    }

    private boolean authenticatedOnline(ClientConnectionSnapshot snapshot, RegisteredNetworkDevice binding) {
        return snapshot != null
                && snapshot.registered()
                && snapshot.status() == DeviceStatus.ONLINE
                && snapshot.clientNetworkIdentityId().equals(binding.networkIdentityId())
                && snapshot.clientInstallationId().equals(binding.installationId())
                && binding.deviceId().equals(snapshot.deviceId())
                && snapshot.connectionId() != null;
    }

    private RemoteOperationOutcome awaitOutcome(DispatchHandle handle) {
        try {
            return handle.completion().get(
                    remoteOperationGateway.resultTimeout().toNanos(), TimeUnit.NANOSECONDS);
        } catch (TimeoutException | ExecutionException exception) {
            return remoteOperationGateway.timeout(handle);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return remoteOperationGateway.timeout(handle);
        }
    }

    private void requireAuthorizedAndStorage() {
        masterAccessGuard.requireAuthorized();
        MasterStorageHealth health = storageState.health();
        if (health.status() != MasterStorageStatus.READY) {
            String code = health.errorCode() == null
                    ? ErrorCode.MASTER_DATABASE_UNAVAILABLE.name()
                    : health.errorCode();
            throw new ApiException(HttpStatus.SERVICE_UNAVAILABLE, code, "Master storage is unavailable.");
        }
    }

    private String required(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST,
                    fieldName + " is required.");
        }
        return value.trim();
    }

    private record TargetPlan(String deviceId, ClientConnectionSnapshot snapshot) {
    }
}
