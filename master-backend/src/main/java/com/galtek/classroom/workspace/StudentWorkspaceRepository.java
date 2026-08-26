package com.galtek.classroom.workspace;

import java.time.OffsetDateTime;
import java.util.Optional;

public interface StudentWorkspaceRepository {

    void create(StudentWorkspace workspace, OffsetDateTime nowUtc);

    Optional<StudentWorkspace> findById(String workspaceId);

    Optional<StudentWorkspace> findByStudentId(String studentId);
}
