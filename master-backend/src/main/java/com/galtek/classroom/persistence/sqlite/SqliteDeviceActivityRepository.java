package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.activity.DeviceActivityEvent;
import com.galtek.classroom.activity.DeviceActivityRepository;
import java.time.OffsetDateTime;
import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;

@Repository
@ConditionalOnProperty(prefix = "galtek.classroom.master.storage", name = "enabled",
        havingValue = "true", matchIfMissing = true)
public class SqliteDeviceActivityRepository implements DeviceActivityRepository {
    private final JdbcTemplate jdbcTemplate;

    public SqliteDeviceActivityRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void add(DeviceActivityEvent event) {
        jdbcTemplate.update("""
                INSERT INTO device_activity_events (
                    event_id, classroom_id, device_id, event_type, actor, occurred_at_utc,
                    role, account_reference, result, message
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                event.eventId(), event.classroomId(), event.deviceId(), event.eventType(), event.actor(),
                event.occurredAtUtc().toString(), event.role(), event.accountReference(),
                event.result(), event.message());
    }

    @Override
    public List<DeviceActivityEvent> find(
            String classroomId,
            String deviceId,
            OffsetDateTime fromInclusive,
            OffsetDateTime toExclusive,
            int limit) {
        return jdbcTemplate.query("""
                SELECT event_id, classroom_id, device_id, event_type, actor, occurred_at_utc,
                       role, account_reference, result, message
                FROM device_activity_events
                WHERE classroom_id = ? AND device_id = ?
                  AND occurred_at_utc >= ? AND occurred_at_utc < ?
                ORDER BY occurred_at_utc DESC, event_id DESC
                LIMIT ?
                """,
                (rs, rowNum) -> new DeviceActivityEvent(
                        rs.getString("event_id"), rs.getString("classroom_id"), rs.getString("device_id"),
                        rs.getString("event_type"), rs.getString("actor"),
                        OffsetDateTime.parse(rs.getString("occurred_at_utc")), rs.getString("role"),
                        rs.getString("account_reference"), rs.getString("result"), rs.getString("message")),
                classroomId, deviceId, fromInclusive.toString(), toExclusive.toString(), limit);
    }
}
