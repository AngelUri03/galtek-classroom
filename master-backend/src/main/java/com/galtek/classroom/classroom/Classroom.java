package com.galtek.classroom.classroom;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import java.util.List;

public record Classroom(
        String classroomId,
        String displayName,
        List<String> deviceIds,
        List<String> studentIds,
        List<String> groupIds,
        ClassroomConfiguration configuration) {

    public Classroom {
        classroomId = requireNonBlank(classroomId, "classroomId");
        displayName = requireNonBlank(displayName, "displayName");
        deviceIds = copyList(deviceIds, "deviceIds");
        studentIds = copyList(studentIds, "studentIds");
        groupIds = copyList(groupIds, "groupIds");
        requireNonNull(configuration, "configuration");
    }
}
