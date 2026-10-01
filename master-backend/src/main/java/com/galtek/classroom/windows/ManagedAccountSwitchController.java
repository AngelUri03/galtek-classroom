package com.galtek.classroom.windows;

import com.galtek.classroom.windows.ManagedAccountSwitchDtos.ManagedAccountSwitchBatchResponse;
import com.galtek.classroom.operations.DeviceMutationHttpGuard;
import java.util.Map;
import java.util.stream.Collectors;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api")
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class ManagedAccountSwitchController {

    private final ManagedAccountSwitchDispatchService dispatchService;
    private final DeviceMutationHttpGuard mutationGuard;

    public ManagedAccountSwitchController(
            ManagedAccountSwitchDispatchService dispatchService,
            DeviceMutationHttpGuard mutationGuard) {
        this.dispatchService = dispatchService;
        this.mutationGuard = mutationGuard;
    }

    @PostMapping("/classrooms/{classroomId}/managed-accounts/switch")
    public ManagedAccountSwitchBatchResponse switchManagedAccount(
            @PathVariable String classroomId,
            @RequestHeader(value = com.galtek.classroom.security.SensitiveActionAuthorizationService.HEADER,
                    required = false) String sensitiveAuthorization,
            @RequestBody(required = false) Map<String, Object> request) {
        String target = request != null && request.get("targetAccountId") instanceof String value ? value : null;
        return mutationGuard.runSession(
                DeviceMutationHttpGuard.targetDeviceIds(request),
                "SWITCH_MANAGED_ACCOUNT",
                target,
                () -> dispatchService.dispatch(classroomId, request, sensitiveAuthorization),
                response -> response.targets().stream().collect(Collectors.toMap(
                        targetResult -> targetResult.deviceId(),
                        targetResult -> targetResult.status())));
    }

    @PostMapping("/operations/{operationId}/retry")
    public ManagedAccountSwitchBatchResponse retryManagedAccountSwitch(
            @PathVariable String operationId,
            @RequestHeader(value = com.galtek.classroom.security.SensitiveActionAuthorizationService.HEADER,
                    required = false) String sensitiveAuthorization,
            @RequestBody(required = false) Map<String, Object> request) {
        // Retry has its own durable claim and authorization must run before the
        // stored operation is read. The normal switch endpoint remains guarded
        // by the per-device coordinator.
        return dispatchService.retry(operationId, request, sensitiveAuthorization);
    }
}
