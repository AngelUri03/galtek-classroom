package com.galtek.classroom.classroom;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;

import com.galtek.classroom.student.StudentPreparationState;
import com.galtek.classroom.student.StudentPreparationStatus;
import java.util.List;

public record ClassroomReadinessPlan(
        String classroomId,
        List<StudentPreparationState> targets) {

    public ClassroomReadinessPlan {
        classroomId = requireNonBlank(classroomId, "classroomId");
        targets = copyList(targets, "targets");
    }

    public int targetCount() {
        return targets.size();
    }

    public List<StudentPreparationState> readyTargets() {
        return targetsByStatus(StudentPreparationStatus.READY);
    }

    public List<StudentPreparationState> preparingTargets() {
        return targets.stream()
                .filter(StudentPreparationState::stillPreparing)
                .toList();
    }

    public List<StudentPreparationState> partialReadyTargets() {
        return targetsByStatus(StudentPreparationStatus.PARTIAL_READY);
    }

    public List<StudentPreparationState> recoveryRequiredTargets() {
        return targetsByStatus(StudentPreparationStatus.RECOVERY_REQUIRED);
    }

    public List<StudentPreparationState> failedTargets() {
        return targetsByStatus(StudentPreparationStatus.FAILED);
    }

    public boolean canStartWithReadyTargets() {
        return !readyTargets().isEmpty();
    }

    public boolean allTargetsReady() {
        return !targets.isEmpty() && readyTargets().size() == targets.size();
    }

    private List<StudentPreparationState> targetsByStatus(StudentPreparationStatus status) {
        return targets.stream()
                .filter(target -> target.status() == status)
                .toList();
    }
}
