package com.galtek.classroom.student;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;

import java.util.List;

public record SchoolGroup(
        String groupId,
        String grade,
        String section,
        String displayName,
        List<String> studentIds) {

    public SchoolGroup {
        groupId = requireNonBlank(groupId, "groupId");
        grade = requireNonBlank(grade, "grade");
        section = requireNonBlank(section, "section");
        displayName = requireNonBlank(displayName, "displayName");
        studentIds = copyList(studentIds, "studentIds");
    }
}
