package com.galtek.classroom.classroom;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface ClassroomRepository {

    void create(Classroom classroom, OffsetDateTime nowUtc);

    Optional<Classroom> findById(String classroomId);

    List<Classroom> findActive();

    long versionOf(String classroomId);

    void updateDisplayName(String classroomId, String displayName, long expectedVersion, OffsetDateTime updatedAtUtc);

    void archive(String classroomId, long expectedVersion, OffsetDateTime updatedAtUtc);

    void authorizeApplication(String classroomId, String applicationId, OffsetDateTime createdAtUtc);
}
