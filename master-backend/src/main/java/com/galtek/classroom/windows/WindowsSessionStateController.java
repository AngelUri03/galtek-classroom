package com.galtek.classroom.windows;

import com.galtek.classroom.windows.WindowsSessionStateDtos.WindowsSessionStateBatchResponse;
import com.galtek.classroom.operations.DeviceMutationHttpGuard;
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
public class WindowsSessionStateController {

    private final WindowsSessionStateReadService readService;
    private final DeviceMutationHttpGuard mutationGuard;

    public WindowsSessionStateController(
            WindowsSessionStateReadService readService,
            DeviceMutationHttpGuard mutationGuard) {
        this.readService = readService;
        this.mutationGuard = mutationGuard;
    }

    @PostMapping("/classrooms/{classroomId}/windows-session-state")
    public WindowsSessionStateBatchResponse read(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        WindowsSessionStateBatchResponse response = readService.read(classroomId, request);
        mutationGuard.confirmStable(response.targets().stream()
                .filter(target -> target.errorCode() == null && !"UNKNOWN".equals(target.state()))
                .map(target -> target.deviceId())
                .toList(), java.util.List.of("SWITCH_MANAGED_ACCOUNT", "LOGOFF_WINDOWS_SESSION"));
        return response;
    }
}
