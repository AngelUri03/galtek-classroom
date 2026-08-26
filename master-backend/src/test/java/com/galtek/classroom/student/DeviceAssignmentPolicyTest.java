package com.galtek.classroom.student;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.PreflightStatus;
import java.time.OffsetDateTime;
import java.util.List;
import org.junit.jupiter.api.Test;

class DeviceAssignmentPolicyTest {

    private final DeviceAssignmentPolicy policy = new DeviceAssignmentPolicy();

    @Test
    void studentCanBeAssignedToFreeDevice() {
        var decision = policy.evaluateNewAssignment("STU-0001", "DEV-PC01", List.of());

        assertThat(decision.ready()).isTrue();
        assertThat(decision.status()).isEqualTo(PreflightStatus.READY);
        assertThat(decision.errors()).isEmpty();
    }

    @Test
    void studentAlreadyAssignedIsDetected() {
        var decision = policy.evaluateNewAssignment(
                "STU-0001",
                "DEV-PC02",
                List.of(currentAssignment("STU-0001", "DEV-PC01")));

        assertThat(decision.ready()).isFalse();
        assertThat(decision.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(decision.errors()).contains(ErrorCode.STUDENT_ALREADY_ASSIGNED);
    }

    @Test
    void occupiedDeviceIsDetected() {
        var decision = policy.evaluateNewAssignment(
                "STU-0002",
                "DEV-PC01",
                List.of(currentAssignment("STU-0001", "DEV-PC01")));

        assertThat(decision.ready()).isFalse();
        assertThat(decision.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(decision.errors()).contains(ErrorCode.TARGET_OCCUPIED);
    }

    private static DeviceAssignment currentAssignment(String studentId, String deviceId) {
        return new DeviceAssignment(
                studentId,
                deviceId,
                OffsetDateTime.parse("2026-08-25T15:00:00Z"),
                DeviceAssignmentStatus.CURRENT,
                DeviceAssignmentSource.MANUAL,
                true);
    }
}
