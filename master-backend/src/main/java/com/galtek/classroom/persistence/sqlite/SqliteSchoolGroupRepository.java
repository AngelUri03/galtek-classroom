package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.student.SchoolGroup;
import com.galtek.classroom.student.SchoolGroupRepository;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;

@Repository
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class SqliteSchoolGroupRepository implements SchoolGroupRepository {

    private final JdbcTemplate jdbcTemplate;

    public SqliteSchoolGroupRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(String classroomId, SchoolGroup group, OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO school_groups (
                        group_id, classroom_id, grade, section, display_name,
                        active, created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, ?, ?, ?, 1, ?, ?, 0)
                    """,
                    group.groupId(),
                    classroomId,
                    group.grade(),
                    group.section(),
                    group.displayName(),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("School group could not be created.", exception);
        }
    }

    @Override
    public Optional<SchoolGroup> findById(String groupId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                "SELECT * FROM school_groups WHERE group_id = ?",
                (rs, rowNum) -> groupFromRow(
                        rs.getString("group_id"),
                        rs.getString("grade"),
                        rs.getString("section"),
                        rs.getString("display_name")),
                groupId);
    }

    @Override
    public List<SchoolGroup> findByClassroomId(String classroomId) {
        return jdbcTemplate.query(
                "SELECT * FROM school_groups WHERE classroom_id = ? AND active = 1 ORDER BY display_name",
                (rs, rowNum) -> groupFromRow(
                        rs.getString("group_id"),
                        rs.getString("grade"),
                        rs.getString("section"),
                        rs.getString("display_name")),
                classroomId);
    }

    private SchoolGroup groupFromRow(String groupId, String grade, String section, String displayName) {
        List<String> studentIds = jdbcTemplate.query(
                "SELECT student_id FROM students WHERE school_group_id = ? ORDER BY display_name",
                (rs, rowNum) -> rs.getString("student_id"),
                groupId);
        return new SchoolGroup(groupId, grade, section, displayName, studentIds);
    }
}
