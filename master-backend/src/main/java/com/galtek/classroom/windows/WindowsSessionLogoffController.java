package com.galtek.classroom.windows;

import com.galtek.classroom.windows.WindowsSessionLogoffDtos.WindowsSessionLogoffBatchResponse;
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
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled",
        havingValue = "true", matchIfMissing = true)
public class WindowsSessionLogoffController {
    private final WindowsSessionLogoffService service;
    private final DeviceMutationHttpGuard mutationGuard;

    public WindowsSessionLogoffController(
            WindowsSessionLogoffService service,
            DeviceMutationHttpGuard mutationGuard) {
        this.service = service;
        this.mutationGuard = mutationGuard;
    }

    @PostMapping("/classrooms/{classroomId}/windows-session/logoff")
    public WindowsSessionLogoffBatchResponse logoff(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        String target = request != null && request.get("accountId") instanceof String value ? value : null;
        return mutationGuard.run(
                DeviceMutationHttpGuard.targetDeviceIds(request),
                "LOGOFF_WINDOWS_SESSION",
                target,
                () -> service.logoff(classroomId, request),
                response -> response.targets().stream().map(targetResult -> targetResult.status()).toList());
    }
}
