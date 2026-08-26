package com.galtek.classroom.student;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface DeviceAssignmentRepository {

    void create(String assignmentId, DeviceAssignment assignment, OffsetDateTime nowUtc);

    Optional<DeviceAssignmentRecord> findCurrentByStudentId(String studentId);

    Optional<DeviceAssignmentRecord> findCurrentByDeviceId(String deviceId);

    List<DeviceAssignmentRecord> findCurrentByClassroomId(String classroomId);

    List<DeviceAssignmentRecord> findHistoryByStudentId(String studentId);

    void endAssignment(String assignmentId, OffsetDateTime endedAtUtc);
}
