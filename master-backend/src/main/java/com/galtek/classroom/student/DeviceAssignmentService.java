package com.galtek.classroom.student;

import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.persistence.MasterStorageException;
import java.time.Clock;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.UUID;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class DeviceAssignmentService {

    private final DeviceAssignmentRepository assignmentRepository;
    private final DeviceAssignmentPolicy assignmentPolicy;
    private final Clock clock;

    public DeviceAssignmentService(
            DeviceAssignmentRepository assignmentRepository,
            Clock clock) {
        this.assignmentRepository = assignmentRepository;
        this.assignmentPolicy = new DeviceAssignmentPolicy();
        this.clock = clock;
    }

    @Transactional
    public DeviceAssignmentRecord assignStudent(
            String studentId,
            String deviceId,
            DeviceAssignmentSource source) {
        OffsetDateTime nowUtc = nowUtc();
        var existing = new ArrayList<DeviceAssignment>();
        assignmentRepository.findCurrentByStudentId(studentId)
                .map(DeviceAssignmentRecord::assignment)
                .ifPresent(existing::add);
        assignmentRepository.findCurrentByDeviceId(deviceId)
                .map(DeviceAssignmentRecord::assignment)
                .filter(assignment -> existing.stream().noneMatch(item -> item.deviceId().equals(assignment.deviceId())
                        && item.studentId().equals(assignment.studentId())))
                .ifPresent(existing::add);

        var decision = assignmentPolicy.evaluateNewAssignment(studentId, deviceId, existing);
        if (!decision.ready()) {
            throw new MasterStorageException(
                    ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION,
                    "Device assignment violates current assignment invariants.");
        }

        var assignment = new DeviceAssignment(
                studentId,
                deviceId,
                nowUtc,
                DeviceAssignmentStatus.CURRENT,
                source,
                true);
        String assignmentId = UUID.randomUUID().toString();
        assignmentRepository.create(assignmentId, assignment, nowUtc);

        return assignmentRepository.findCurrentByStudentId(studentId)
                .orElseThrow(() -> new MasterStorageException(
                        ErrorCode.MASTER_DATABASE_UNAVAILABLE,
                        "Current assignment could not be reloaded."));
    }

    @Transactional
    public DeviceAssignmentRecord moveCurrentAssignment(
            String studentId,
            String targetDeviceId,
            DeviceAssignmentSource source) {
        var current = assignmentRepository.findCurrentByStudentId(studentId)
                .orElseThrow(() -> new MasterStorageException(
                        ErrorCode.PERSISTENCE_CONSTRAINT_VIOLATION,
                        "Student does not have a current assignment."));
        assignmentRepository.endAssignment(current.assignmentId(), nowUtc());
        return assignStudent(studentId, targetDeviceId, source);
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }
}
