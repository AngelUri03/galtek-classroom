package com.galtek.classroom.student;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.PreflightStatus;
import com.galtek.classroom.workspace.LogicalWorkspaceDestination;
import com.galtek.classroom.workspace.StudentWorkspace;
import com.galtek.classroom.workspace.WorkspaceRecoveryPolicy;
import com.galtek.classroom.workspace.WorkspaceStatus;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;

class StudentMovePlannerTest {

    private final StudentMovePlanner planner = new StudentMovePlanner();

    @Test
    void validSourceAndTargetAreReady() {
        var student = student("STU-0001");
        var source = device("DEV-PC01", DeviceStatus.ONLINE);
        var target = device("DEV-PC12", DeviceStatus.ONLINE);

        var plan = planner.planMove(
                "OP-MOVE-1",
                student,
                target,
                List.of(source, target),
                List.of(currentAssignment("STU-0001", "DEV-PC01")),
                workspace("STU-0001", WorkspaceStatus.READY));

        assertThat(plan.ready()).isTrue();
        assertThat(plan.status()).isEqualTo(PreflightStatus.READY);
        assertThat(plan.errors()).isEmpty();
    }

    @Test
    void occupiedTargetIsBlocked() {
        var student = student("STU-0001");
        var source = device("DEV-PC01", DeviceStatus.ONLINE);
        var target = device("DEV-PC12", DeviceStatus.ONLINE);

        var plan = planner.planMove(
                "OP-MOVE-2",
                student,
                target,
                List.of(source, target),
                List.of(
                        currentAssignment("STU-0001", "DEV-PC01"),
                        currentAssignment("STU-0002", "DEV-PC12")),
                workspace("STU-0001", WorkspaceStatus.READY));

        assertThat(plan.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(plan.errors()).contains(ErrorCode.TARGET_OCCUPIED);
    }

    @Test
    void sourceOfflineIsBlocked() {
        var student = student("STU-0001");
        var source = device("DEV-PC01", DeviceStatus.OFFLINE);
        var target = device("DEV-PC12", DeviceStatus.ONLINE);

        var plan = planner.planMove(
                "OP-MOVE-3",
                student,
                target,
                List.of(source, target),
                List.of(currentAssignment("STU-0001", "DEV-PC01")),
                workspace("STU-0001", WorkspaceStatus.READY));

        assertThat(plan.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(plan.errors()).contains(ErrorCode.SOURCE_DEVICE_UNAVAILABLE);
    }

    @Test
    void targetOfflineIsBlocked() {
        var student = student("STU-0001");
        var source = device("DEV-PC01", DeviceStatus.ONLINE);
        var target = device("DEV-PC12", DeviceStatus.OFFLINE);

        var plan = planner.planMove(
                "OP-MOVE-4",
                student,
                target,
                List.of(source, target),
                List.of(currentAssignment("STU-0001", "DEV-PC01")),
                workspace("STU-0001", WorkspaceStatus.READY));

        assertThat(plan.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(plan.errors()).contains(ErrorCode.TARGET_DEVICE_UNAVAILABLE);
    }

    @Test
    void busyWorkspaceIsBlocked() {
        var student = student("STU-0001");
        var source = device("DEV-PC01", DeviceStatus.ONLINE);
        var target = device("DEV-PC12", DeviceStatus.ONLINE);

        var plan = planner.planMove(
                "OP-MOVE-5",
                student,
                target,
                List.of(source, target),
                List.of(currentAssignment("STU-0001", "DEV-PC01")),
                workspace("STU-0001", WorkspaceStatus.BUSY));

        assertThat(plan.status()).isEqualTo(PreflightStatus.BLOCKED);
        assertThat(plan.errors()).contains(ErrorCode.WORKSPACE_BUSY);
    }

    private static Student student(String studentId) {
        return new Student(
                studentId,
                "Alumno",
                studentId.substring(studentId.length() - 4),
                "Alumno " + studentId.substring(studentId.length() - 4),
                "3",
                "B",
                true,
                "WS-" + studentId,
                "BROWSER-" + studentId,
                null);
    }

    private static Device device(String deviceId, DeviceStatus status) {
        return new Device(
                deviceId,
                "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                deviceId.replace("DEV-", ""),
                deviceId.replace("DEV-", ""),
                status,
                OffsetDateTime.parse("2026-08-25T15:00:00Z"),
                Set.of(DeviceCapability.SESSION_AGENT, DeviceCapability.WORKSPACE_FILES),
                null);
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

    private static StudentWorkspace workspace(String studentId, WorkspaceStatus status) {
        return new StudentWorkspace(
                "WS-" + studentId,
                studentId,
                status,
                Set.of(LogicalWorkspaceDestination.DOCUMENTS, LogicalWorkspaceDestination.HOMEWORK),
                "BROWSER-" + studentId,
                WorkspaceRecoveryPolicy.plannedDefaults());
    }
}
