package com.galtek.classroom.workspace;

import java.time.Clock;
import java.time.OffsetDateTime;
import java.util.Optional;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class WorkspaceMetadataService {

    private final StudentWorkspaceRepository workspaceRepository;
    private final Clock clock;

    public WorkspaceMetadataService(StudentWorkspaceRepository workspaceRepository, Clock clock) {
        this.workspaceRepository = workspaceRepository;
        this.clock = clock;
    }

    @Transactional
    public StudentWorkspace create(StudentWorkspace workspace) {
        workspaceRepository.create(workspace, nowUtc());
        return workspace;
    }

    public Optional<StudentWorkspace> findByStudentId(String studentId) {
        return workspaceRepository.findByStudentId(studentId);
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }
}
