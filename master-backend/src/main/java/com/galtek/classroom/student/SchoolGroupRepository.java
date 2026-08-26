package com.galtek.classroom.student;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface SchoolGroupRepository {

    void create(String classroomId, SchoolGroup group, OffsetDateTime nowUtc);

    Optional<SchoolGroup> findById(String groupId);

    List<SchoolGroup> findByClassroomId(String classroomId);
}
