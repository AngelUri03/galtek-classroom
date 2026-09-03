package com.galtek.classroom.application;

import com.galtek.classroom.operations.OperationDtos.OperationBatchResponse;
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
public class OpenApplicationController {

    private final OpenApplicationDispatchService dispatchService;

    public OpenApplicationController(OpenApplicationDispatchService dispatchService) {
        this.dispatchService = dispatchService;
    }

    @PostMapping("/classrooms/{classroomId}/open-application")
    public OperationBatchResponse openApplication(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        return dispatchService.dispatch(classroomId, request);
    }
}
