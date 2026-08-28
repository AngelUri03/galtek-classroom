package com.galtek.classroom.classroom;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.student.StudentPreparationStage;
import com.galtek.classroom.student.StudentPreparationState;
import java.util.List;
import org.junit.jupiter.api.Test;

class ClassroomReadinessPlanTest {

    @Test
    void partialClassroomReadinessKeepsDevicesIndependent() {
        var plan = new ClassroomReadinessPlan(
                "CLS-PRIMARY",
                List.of(
                        StudentPreparationState.ready("DEV-PC01", "STU-001"),
                        StudentPreparationState.ready("DEV-PC02", "STU-002"),
                        StudentPreparationState.inProgress(
                                "DEV-PC03",
                                "STU-003",
                                StudentPreparationStage.PREPARING_WORKSPACE),
                        StudentPreparationState.failed(
                                "DEV-PC04",
                                "STU-004",
                                StudentPreparationStage.PREPARING_WINDOWS_SESSION,
                                List.of(ErrorCode.DEVICE_OFFLINE)),
                        StudentPreparationState.recoveryRequired(
                                "DEV-PC05",
                                "STU-005",
                                StudentPreparationStage.PREPARING_WORKSPACE,
                                List.of(ErrorCode.WORKSPACE_NOT_READY))));

        assertThat(plan.targetCount()).isEqualTo(5);
        assertThat(plan.readyTargets())
                .extracting(StudentPreparationState::deviceId)
                .containsExactly("DEV-PC01", "DEV-PC02");
        assertThat(plan.preparingTargets())
                .extracting(StudentPreparationState::deviceId)
                .containsExactly("DEV-PC03");
        assertThat(plan.failedTargets())
                .extracting(StudentPreparationState::deviceId)
                .containsExactly("DEV-PC04");
        assertThat(plan.recoveryRequiredTargets())
                .extracting(StudentPreparationState::deviceId)
                .containsExactly("DEV-PC05");
        assertThat(plan.canStartWithReadyTargets()).isTrue();
        assertThat(plan.allTargetsReady()).isFalse();
    }
}
