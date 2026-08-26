package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.core.RowMapper;
import org.springframework.stereotype.Repository;

@Repository
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class SqliteDeviceRepository implements DeviceRepository {

    private static final String DEVICE_SELECT = """
            SELECT d.*, da.student_id AS assigned_student_id
            FROM devices d
            LEFT JOIN device_assignments da ON da.device_id = d.device_id AND da.current = 1
            """;

    private static final RowMapper<Device> ROW_MAPPER = (rs, rowNum) -> new Device(
            rs.getString("device_id"),
            rs.getString("installation_id"),
            rs.getString("display_name"),
            rs.getString("hostname"),
            DeviceStatus.valueOf(rs.getString("status")),
            UtcTimestamps.fromText(rs.getString("last_seen_utc")),
            JsonText.enumSet(rs.getString("capabilities_json"), DeviceCapability.class),
            rs.getString("assigned_student_id"));

    private final JdbcTemplate jdbcTemplate;

    public SqliteDeviceRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(String classroomId, Device device, OffsetDateTime nowUtc) {
        try {
            jdbcTemplate.update("""
                    INSERT INTO devices (
                        device_id, classroom_id, installation_id, display_name, hostname,
                        status, last_seen_utc, capabilities_json, active,
                        created_at_utc, updated_at_utc, version
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, 0)
                    """,
                    device.deviceId(),
                    classroomId,
                    device.installationId(),
                    device.displayName(),
                    device.hostname(),
                    device.status().name(),
                    UtcTimestamps.toText(device.lastSeen()),
                    JsonText.enumNames(device.capabilities()),
                    UtcTimestamps.toText(nowUtc),
                    UtcTimestamps.toText(nowUtc));
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Device could not be created.", exception);
        }
    }

    @Override
    public Optional<Device> findById(String deviceId) {
        return SqliteJdbc.optional(
                jdbcTemplate,
                DEVICE_SELECT + " WHERE d.device_id = ?",
                ROW_MAPPER,
                deviceId);
    }

    @Override
    public List<Device> findByClassroomId(String classroomId) {
        return jdbcTemplate.query(
                DEVICE_SELECT + " WHERE d.classroom_id = ? AND d.active = 1 ORDER BY d.display_name",
                ROW_MAPPER,
                classroomId);
    }
}
