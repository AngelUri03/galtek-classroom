package com.galtek.classroom.windows;

import com.galtek.classroom.windows.WindowsSessionStateDtos.WindowsSessionStateBatchResponse;
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

    public WindowsSessionStateController(WindowsSessionStateReadService readService) {
        this.readService = readService;
    }

    @PostMapping("/classrooms/{classroomId}/windows-session-state")
    public WindowsSessionStateBatchResponse read(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        return readService.read(classroomId, request);
    }
}
