package com.galtek.classroom.classroom;

import java.time.Clock;
import java.time.OffsetDateTime;
import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class ClassroomManagementService {

    private final ClassroomRepository classroomRepository;
    private final Clock clock;

    public ClassroomManagementService(ClassroomRepository classroomRepository, Clock clock) {
        this.classroomRepository = classroomRepository;
        this.clock = clock;
    }

    @Transactional
    public Classroom create(Classroom classroom) {
        classroomRepository.create(classroom, nowUtc());
        return classroom;
    }

    public List<Classroom> activeClassrooms() {
        return classroomRepository.findActive();
    }

    @Transactional
    public void rename(String classroomId, String displayName, long expectedVersion) {
        classroomRepository.updateDisplayName(classroomId, displayName, expectedVersion, nowUtc());
    }

    @Transactional
    public void archive(String classroomId, long expectedVersion) {
        classroomRepository.archive(classroomId, expectedVersion, nowUtc());
    }

    @Transactional
    public void authorizeApplication(String classroomId, String applicationId) {
        classroomRepository.authorizeApplication(classroomId, applicationId, nowUtc());
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }
}
