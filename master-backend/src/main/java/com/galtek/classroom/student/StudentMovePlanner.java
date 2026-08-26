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

public final class StudentMovePlanner {

    public StudentMovePlan planMove(
            String operationId,
            Student student,
            Device targetDevice,
            List<Device> knownDevices,
            List<DeviceAssignment> assignments,
            StudentWorkspace workspace) {
        operationId = requireNonBlank(operationId, "operationId");
        requireNonNull(student, "student");
        requireNonNull(targetDevice, "targetDevice");
        knownDevices = copyList(knownDevices, "knownDevices");
        assignments = copyList(assignments, "assignments");
        requireNonNull(workspace, "workspace");

        var errors = new ArrayList<ErrorCode>();
        var currentAssignment = currentAssignmentFor(student.studentId(), assignments);
        var sourceDeviceId = currentAssignment == null ? null : currentAssignment.deviceId();

        if (currentAssignment == null) {
            errors.add(ErrorCode.STUDENT_NOT_ASSIGNED);
        }

        var sourceDevice = sourceDeviceId == null
                ? null
                : knownDevices.stream()
                .filter(device -> device.deviceId().equals(sourceDeviceId))
                .findFirst()
                .orElse(null);

        if (currentAssignment != null && (sourceDevice == null || !sourceDevice.availableForInteractiveOperation())) {
            errors.add(ErrorCode.SOURCE_DEVICE_UNAVAILABLE);
        }

        if (!targetDevice.availableForInteractiveOperation()) {
            errors.add(ErrorCode.TARGET_DEVICE_UNAVAILABLE);
        }

        var targetOccupied = assignments.stream()
                .filter(DeviceAssignment::activeCurrent)
                .anyMatch(assignment -> assignment.deviceId().equals(targetDevice.deviceId())
                        && !assignment.studentId().equals(student.studentId()));
        if (targetOccupied) {
            errors.add(ErrorCode.TARGET_OCCUPIED);
        }

        if (!workspace.studentId().equals(student.studentId())) {
            errors.add(ErrorCode.WORKSPACE_NOT_FOUND);
        } else if (workspace.status().busy()) {
            errors.add(ErrorCode.WORKSPACE_BUSY);
        } else if (workspace.status() != WorkspaceStatus.READY) {
            errors.add(ErrorCode.WORKSPACE_NOT_READY);
        }

        return new StudentMovePlan(
                operationId,
                student.studentId(),
                sourceDeviceId,
                targetDevice.deviceId(),
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
}
