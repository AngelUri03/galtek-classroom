package com.galtek.classroom.application;

import com.galtek.classroom.operations.OperationDtos.OperationBatchResponse;
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
public class OpenApplicationController {

    private final OpenApplicationDispatchService dispatchService;
    private final DeviceMutationHttpGuard mutationGuard;

    public OpenApplicationController(
            OpenApplicationDispatchService dispatchService,
            DeviceMutationHttpGuard mutationGuard) {
        this.dispatchService = dispatchService;
        this.mutationGuard = mutationGuard;
    }

    @PostMapping("/classrooms/{classroomId}/open-application")
    public OperationBatchResponse openApplication(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        return mutationGuard.run(
                DeviceMutationHttpGuard.targetDeviceIds(request),
                "OPEN_APPLICATION",
                null,
                () -> dispatchService.dispatch(classroomId, request),
                response -> response.targets().stream().map(target -> target.status()).toList());
    }
}
