package com.galtek.classroom.student;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.device.Device;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.MoveWorkflowState;
import com.galtek.classroom.operations.PreflightStatus;
import com.galtek.classroom.workspace.StudentWorkspace;
import com.galtek.classroom.workspace.WorkspaceStatus;
import java.util.ArrayList;
import java.util.List;

public final class StudentSwapPlanner {

    public StudentSwapPlan planSwap(
            String operationId,
            Student leftStudent,
            Student rightStudent,
            List<Device> knownDevices,
            List<DeviceAssignment> assignments,
            StudentWorkspace leftWorkspace,
            StudentWorkspace rightWorkspace) {
        operationId = requireNonBlank(operationId, "operationId");
        requireNonNull(leftStudent, "leftStudent");
        requireNonNull(rightStudent, "rightStudent");
        knownDevices = copyList(knownDevices, "knownDevices");
        assignments = copyList(assignments, "assignments");
        requireNonNull(leftWorkspace, "leftWorkspace");
        requireNonNull(rightWorkspace, "rightWorkspace");

        var errors = new ArrayList<ErrorCode>();
        if (leftStudent.studentId().equals(rightStudent.studentId())) {
            errors.add(ErrorCode.SELF_SWAP_NOT_ALLOWED);
        }

        var leftAssignment = currentAssignmentFor(leftStudent.studentId(), assignments);
        var rightAssignment = currentAssignmentFor(rightStudent.studentId(), assignments);
        var leftDeviceId = leftAssignment == null ? null : leftAssignment.deviceId();
        var rightDeviceId = rightAssignment == null ? null : rightAssignment.deviceId();

        if (leftAssignment == null || rightAssignment == null) {
            errors.add(ErrorCode.STUDENT_NOT_ASSIGNED);
        }

        if (leftDeviceId != null && leftDeviceId.equals(rightDeviceId)) {
            errors.add(ErrorCode.SAME_DEVICE_ASSIGNMENT);
        }

        var leftDevice = findDevice(leftDeviceId, knownDevices);
        var rightDevice = findDevice(rightDeviceId, knownDevices);

        if (leftAssignment != null && (leftDevice == null || !leftDevice.availableForInteractiveOperation())) {
            errors.add(ErrorCode.SOURCE_DEVICE_UNAVAILABLE);
        }

        if (rightAssignment != null && (rightDevice == null || !rightDevice.availableForInteractiveOperation())) {
            errors.add(ErrorCode.TARGET_DEVICE_UNAVAILABLE);
        }

        validateWorkspace(leftWorkspace, leftStudent.studentId(), errors);
        validateWorkspace(rightWorkspace, rightStudent.studentId(), errors);

        return new StudentSwapPlan(
                operationId,
                leftStudent.studentId(),
                rightStudent.studentId(),
                leftDeviceId,
                rightDeviceId,
                errors.isEmpty() ? PreflightStatus.READY : PreflightStatus.BLOCKED,
                MoveWorkflowState.PREFLIGHT,
                errors);
    }

    private static DeviceAssignment currentAssignmentFor(String studentId, List<DeviceAssignment> assignments) {
        return assignments.stream()
                .filter(DeviceAssignment::activeCurrent)
                .filter(assignment -> assignment.studentId().equals(studentId))
                .findFirst()
                .orElse(null);
    }

    private static Device findDevice(String deviceId, List<Device> knownDevices) {
        if (deviceId == null) {
            return null;
        }

        return knownDevices.stream()
                .filter(device -> device.deviceId().equals(deviceId))
                .findFirst()
                .orElse(null);
    }

    private static void validateWorkspace(StudentWorkspace workspace, String studentId, List<ErrorCode> errors) {
        if (!workspace.studentId().equals(studentId)) {
            errors.add(ErrorCode.WORKSPACE_NOT_FOUND);
        } else if (workspace.status().busy()) {
            errors.add(ErrorCode.WORKSPACE_BUSY);
        } else if (workspace.status() != WorkspaceStatus.READY) {
            errors.add(ErrorCode.WORKSPACE_NOT_READY);
        }
    }
}
