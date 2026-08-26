package com.galtek.classroom.student;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface StudentRepository {

    void create(String classroomId, String schoolGroupId, Student student, OffsetDateTime nowUtc);

    Optional<Student> findById(String studentId);

    List<Student> findByGroupId(String schoolGroupId);

    List<Student> findActiveByClassroomId(String classroomId);

    long versionOf(String studentId);

    void archive(String studentId, long expectedVersion, OffsetDateTime updatedAtUtc);
}
