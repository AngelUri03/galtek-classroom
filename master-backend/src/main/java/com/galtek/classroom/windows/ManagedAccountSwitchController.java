package com.galtek.classroom.windows;

import com.galtek.classroom.windows.ManagedAccountSwitchDtos.ManagedAccountSwitchBatchResponse;
import java.util.Map;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
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
public class ManagedAccountSwitchController {

    private final ManagedAccountSwitchDispatchService dispatchService;

    public ManagedAccountSwitchController(ManagedAccountSwitchDispatchService dispatchService) {
        this.dispatchService = dispatchService;
    }

    @PostMapping("/classrooms/{classroomId}/managed-accounts/switch")
    public ManagedAccountSwitchBatchResponse switchManagedAccount(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        return dispatchService.dispatch(classroomId, request);
    }

    @PostMapping("/operations/{operationId}/retry")
    public ManagedAccountSwitchBatchResponse retryManagedAccountSwitch(
            @PathVariable String operationId,
            @RequestBody(required = false) Map<String, Object> request) {
        return dispatchService.retry(operationId, request);
    }
}
