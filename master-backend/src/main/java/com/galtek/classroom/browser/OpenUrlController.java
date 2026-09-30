package com.galtek.classroom.browser;

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
public class OpenUrlController {

    private final OpenUrlDispatchService dispatchService;
    private final DeviceMutationHttpGuard mutationGuard;

    public OpenUrlController(OpenUrlDispatchService dispatchService, DeviceMutationHttpGuard mutationGuard) {
        this.dispatchService = dispatchService;
        this.mutationGuard = mutationGuard;
    }

    @PostMapping("/classrooms/{classroomId}/open-url")
    public OperationBatchResponse openUrl(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        return mutationGuard.run(
                DeviceMutationHttpGuard.targetDeviceIds(request),
                "OPEN_URL",
                null,
                () -> dispatchService.dispatch(classroomId, request),
                response -> response.targets().stream().map(target -> target.status()).toList());
    }
}
