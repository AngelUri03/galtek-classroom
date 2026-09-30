package com.galtek.classroom.network;

import com.galtek.classroom.network.NetworkDtos.DeviceRegistrationResponse;
import com.galtek.classroom.network.NetworkDtos.NetworkClientResponse;
import com.galtek.classroom.network.NetworkDtos.PowerControlBatchResponse;
import com.galtek.classroom.network.NetworkDtos.RegisterDeviceRequest;
import com.galtek.classroom.operations.DeviceMutationHttpGuard;
import java.util.List;
import java.util.Map;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api")
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class NetworkClientController {

    private final NetworkClientAdminService service;
    private final PowerControlDispatchService powerControlDispatchService;
    private final DeviceMutationHttpGuard mutationGuard;

    public NetworkClientController(
            NetworkClientAdminService service,
            PowerControlDispatchService powerControlDispatchService,
            DeviceMutationHttpGuard mutationGuard) {
        this.service = service;
        this.powerControlDispatchService = powerControlDispatchService;
        this.mutationGuard = mutationGuard;
    }

    @GetMapping("/network/clients")
    public List<NetworkClientResponse> clients() {
        return service.clients();
    }

    @PostMapping("/classrooms/{classroomId}/devices/register")
    public ResponseEntity<DeviceRegistrationResponse> registerDevice(
            @PathVariable String classroomId,
            @RequestBody(required = false) RegisterDeviceRequest request) {
        return ResponseEntity.status(HttpStatus.CREATED)
                .body(service.registerDevice(classroomId, request));
    }

    @PostMapping("/classrooms/{classroomId}/power-control")
    public PowerControlBatchResponse powerControl(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        String type = request != null && request.get("type") instanceof String value ? value : "POWER_CONTROL";
        return mutationGuard.run(
                DeviceMutationHttpGuard.targetDeviceIds(request),
                type,
                null,
                () -> powerControlDispatchService.dispatch(classroomId, request),
                response -> response.targets().stream().map(target -> target.status()).toList());
    }
}
