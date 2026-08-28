package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.network.DeviceNetworkBinding;
import com.galtek.classroom.network.DeviceNetworkBindingRepository;
import com.galtek.classroom.network.RegisteredNetworkDevice;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.OffsetDateTime;
import java.util.Collection;
import java.util.Collections;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.TreeSet;
import java.util.UUID;
import java.util.function.Supplier;
import java.util.stream.Collectors;
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
public class SqliteDeviceNetworkBindingRepository implements DeviceNetworkBindingRepository {

    private static final String REGISTERED_SELECT = """
            SELECT b.*,
                   d.classroom_id,
                   d.display_name,
                   d.hostname,
                   d.active AS device_active
            FROM device_network_bindings b
            INNER JOIN devices d ON d.device_id = b.device_id
            WHERE b.current = 1
            """;

    private static final RowMapper<RegisteredNetworkDevice> REGISTERED_MAPPER =
            (rs, rowNum) -> registeredFromRow(rs);

    private final JdbcTemplate jdbcTemplate;

    public SqliteDeviceNetworkBindingRepository(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @Override
    public void create(DeviceNetworkBinding binding, OffsetDateTime nowUtc) {
        execute(() -> jdbcTemplate.update("""
                INSERT INTO device_network_bindings (
                    binding_id, device_id, installation_id, network_identity_id,
                    public_key_fingerprint, agent_version, capabilities_json,
                    registered_at_utc, last_connected_at_utc, current,
                    created_at_utc, updated_at_utc, version
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)
                """,
                binding.bindingId(),
                binding.deviceId(),
                binding.installationId().toString(),
                binding.networkIdentityId().toString(),
                binding.publicKeyFingerprint(),
                emptyToNull(binding.agentVersion()),
                JsonText.enumNames(binding.capabilities()),
                UtcTimestamps.toText(binding.registeredAtUtc()),
                UtcTimestamps.toText(binding.lastConnectedAtUtc()),
                SqliteJdbc.bool(binding.current()),
                UtcTimestamps.toText(nowUtc),
                UtcTimestamps.toText(nowUtc)), "Device network binding could not be created.");
    }

    @Override
    public Optional<RegisteredNetworkDevice> findCurrentByNetworkIdentityId(UUID networkIdentityId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                REGISTERED_SELECT + " AND b.network_identity_id = ?",
                REGISTERED_MAPPER,
                networkIdentityId.toString()));
    }

    @Override
    public Optional<RegisteredNetworkDevice> findCurrentByInstallationId(UUID installationId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                REGISTERED_SELECT + " AND b.installation_id = ?",
                REGISTERED_MAPPER,
                installationId.toString()));
    }

    @Override
    public Optional<RegisteredNetworkDevice> findCurrentByDeviceId(String deviceId) {
        return query(() -> SqliteJdbc.optional(
                jdbcTemplate,
                REGISTERED_SELECT + " AND b.device_id = ?",
                REGISTERED_MAPPER,
                deviceId));
    }

    @Override
    public List<RegisteredNetworkDevice> findCurrentByDeviceIds(Collection<String> deviceIds) {
        if (deviceIds == null || deviceIds.isEmpty()) {
            return List.of();
        }

        return query(() -> {
            List<String> ids = distinct(deviceIds);
            if (ids.isEmpty()) {
                return List.of();
            }

            return jdbcTemplate.query(
                    REGISTERED_SELECT + " AND b.device_id IN (%s)".formatted(placeholders(ids.size())),
                    REGISTERED_MAPPER,
                    ids.toArray());
        });
    }

    @Override
    public List<RegisteredNetworkDevice> findAllCurrent() {
        return query(() -> jdbcTemplate.query(
                REGISTERED_SELECT + " ORDER BY d.display_name",
                REGISTERED_MAPPER));
    }

    @Override
    public boolean activeDeviceExistsForInstallation(UUID installationId) {
        return query(() -> {
            Integer count = jdbcTemplate.queryForObject("""
                    SELECT COUNT(*)
                    FROM devices
                    WHERE installation_id = ? AND active = 1
                    """,
                    Integer.class,
                    installationId.toString());
            return count != null && count > 0;
        });
    }

    @Override
    public void recordConnection(
            UUID networkIdentityId,
            String agentVersion,
            Set<DeviceCapability> capabilities,
            OffsetDateTime connectedAtUtc,
            OffsetDateTime nowUtc) {
        execute(() -> jdbcTemplate.update("""
                UPDATE device_network_bindings
                SET agent_version = ?,
                    capabilities_json = ?,
                    last_connected_at_utc = ?,
                    updated_at_utc = ?,
                    version = version + 1
                WHERE network_identity_id = ? AND current = 1
                """,
                emptyToNull(agentVersion),
                JsonText.enumNames(capabilities == null ? Set.of() : capabilities),
                UtcTimestamps.toText(connectedAtUtc),
                UtcTimestamps.toText(nowUtc),
                networkIdentityId.toString()), "Device network binding connection could not be recorded.");
    }

    private static RegisteredNetworkDevice registeredFromRow(ResultSet rs) throws SQLException {
        Set<DeviceCapability> capabilities = JsonText.enumSet(rs.getString("capabilities_json"), DeviceCapability.class)
                .stream()
                .collect(Collectors.toCollection(TreeSet::new));
        return new RegisteredNetworkDevice(
                rs.getString("binding_id"),
                rs.getString("device_id"),
                rs.getString("classroom_id"),
                UUID.fromString(rs.getString("installation_id")),
                UUID.fromString(rs.getString("network_identity_id")),
                rs.getString("public_key_fingerprint"),
                rs.getString("display_name"),
                rs.getString("hostname"),
                rs.getString("agent_version"),
                capabilities,
                UtcTimestamps.fromText(rs.getString("registered_at_utc")),
                UtcTimestamps.fromText(rs.getString("last_connected_at_utc")),
                SqliteJdbc.bool(rs, "device_active"),
                rs.getLong("version"));
    }

    private <T> T query(Supplier<T> supplier) {
        try {
            return supplier.get();
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map("Device network binding query failed.", exception);
        }
    }

    private void execute(Runnable runnable, String message) {
        try {
            runnable.run();
        } catch (DataAccessException exception) {
            throw SqliteExceptionMapper.map(message, exception);
        }
    }

    private static String emptyToNull(String value) {
        return value == null || value.isBlank() ? null : value.trim();
    }

    private static List<String> distinct(Collection<String> values) {
        return values.stream()
                .filter(value -> value != null && !value.isBlank())
                .distinct()
                .toList();
    }

    private static String placeholders(int count) {
        return String.join(",", Collections.nCopies(count, "?"));
    }
}
