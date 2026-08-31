package com.galtek.classroom.network;

import com.galtek.classroom.network.NetworkDtos.DeviceRegistrationResponse;
import com.galtek.classroom.network.NetworkDtos.NetworkClientResponse;
import com.galtek.classroom.network.NetworkDtos.PowerControlBatchResponse;
import com.galtek.classroom.network.NetworkDtos.RegisterDeviceRequest;
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

    public NetworkClientController(
            NetworkClientAdminService service,
            PowerControlDispatchService powerControlDispatchService) {
        this.service = service;
        this.powerControlDispatchService = powerControlDispatchService;
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
        return powerControlDispatchService.dispatch(classroomId, request);
    }
}
