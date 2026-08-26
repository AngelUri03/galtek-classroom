package com.galtek.classroom.student;

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
public class StudentManagementService {

    private final SchoolGroupRepository schoolGroupRepository;
    private final StudentRepository studentRepository;
    private final Clock clock;

    public StudentManagementService(
            SchoolGroupRepository schoolGroupRepository,
            StudentRepository studentRepository,
            Clock clock) {
        this.schoolGroupRepository = schoolGroupRepository;
        this.studentRepository = studentRepository;
        this.clock = clock;
    }

    @Transactional
    public SchoolGroup createGroup(String classroomId, SchoolGroup group) {
        schoolGroupRepository.create(classroomId, group, nowUtc());
        return group;
    }

    @Transactional
    public Student register(String classroomId, String schoolGroupId, Student student) {
        studentRepository.create(classroomId, schoolGroupId, student, nowUtc());
        return student;
    }

    public List<Student> activeStudentsByClassroom(String classroomId) {
        return studentRepository.findActiveByClassroomId(classroomId);
    }

    public List<Student> studentsByGroup(String schoolGroupId) {
        return studentRepository.findByGroupId(schoolGroupId);
    }

    @Transactional
    public void archive(String studentId, long expectedVersion) {
        studentRepository.archive(studentId, expectedVersion, nowUtc());
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }
}
