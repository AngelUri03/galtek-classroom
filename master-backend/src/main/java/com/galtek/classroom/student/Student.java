package com.galtek.classroom.student;

import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;

public record Student(
        String studentId,
        String firstName,
        String lastName,
        String displayName,
        String grade,
        String group,
        boolean active,
        String workspaceId,
        String browserProfileId,
        DeviceAssignment currentDeviceAssignment) {

    public Student {
        studentId = requireNonBlank(studentId, "studentId");
        firstName = requireNonBlank(firstName, "firstName");
        lastName = requireNonBlank(lastName, "lastName");
        displayName = requireNonBlank(displayName, "displayName");
        grade = requireNonBlank(grade, "grade");
        group = requireNonBlank(group, "group");
        workspaceId = requireNonBlank(workspaceId, "workspaceId");
        browserProfileId = requireNonBlank(browserProfileId, "browserProfileId");
        if (currentDeviceAssignment != null && !studentId.equals(currentDeviceAssignment.studentId())) {
            throw new IllegalArgumentException("currentDeviceAssignment must belong to the same student.");
        }
    }
}
