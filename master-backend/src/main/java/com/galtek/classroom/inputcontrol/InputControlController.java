package com.galtek.classroom.inputcontrol;

import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.master.MasterUnlockAccessGuard;
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
public class InputControlController {

    private final MasterAccessGuard masterAccessGuard;
    private final MasterUnlockAccessGuard masterUnlockAccessGuard;
    private final InputControlDispatchService dispatchService;
    private final DeviceMutationHttpGuard mutationGuard;

    public InputControlController(
            MasterAccessGuard masterAccessGuard,
            MasterUnlockAccessGuard masterUnlockAccessGuard,
            InputControlDispatchService dispatchService,
            DeviceMutationHttpGuard mutationGuard) {
        this.masterAccessGuard = masterAccessGuard;
        this.masterUnlockAccessGuard = masterUnlockAccessGuard;
        this.dispatchService = dispatchService;
        this.mutationGuard = mutationGuard;
    }

    @PostMapping("/classrooms/{classroomId}/input-control/lock")
    public OperationBatchResponse lock(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        masterAccessGuard.requireAuthorized();
        return mutationGuard.run(
                DeviceMutationHttpGuard.targetDeviceIds(request),
                "LOCK_INPUT",
                null,
                () -> dispatchService.dispatchLock(classroomId, request),
                response -> response.targets().stream().map(target -> target.status()).toList());
    }

    @PostMapping("/classrooms/{classroomId}/input-control/unlock")
    public OperationBatchResponse unlock(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        masterUnlockAccessGuard.requireUnlockAuthorized();
        return mutationGuard.run(
                DeviceMutationHttpGuard.targetDeviceIds(request),
                "UNLOCK_INPUT",
                null,
                () -> dispatchService.dispatchUnlock(classroomId, request),
                response -> response.targets().stream().map(target -> target.status()).toList());
    }
}
