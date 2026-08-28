-- Device registration over paired Network Identities.
-- Pairing trust remains authoritative in paired-clients.json; this table only
-- binds a Master-controlled Device to an already paired Client identity.

CREATE TABLE device_network_bindings (
    binding_id TEXT PRIMARY KEY,
    device_id TEXT NOT NULL,
    installation_id TEXT NOT NULL,
    network_identity_id TEXT NOT NULL,
    public_key_fingerprint TEXT NOT NULL,
    agent_version TEXT,
    capabilities_json TEXT NOT NULL,
    registered_at_utc TEXT NOT NULL,
    last_connected_at_utc TEXT,
    current INTEGER NOT NULL DEFAULT 1 CHECK (current IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    FOREIGN KEY (device_id) REFERENCES devices(device_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX uq_device_network_bindings_current_device
ON device_network_bindings(device_id)
WHERE current = 1;

CREATE UNIQUE INDEX uq_device_network_bindings_current_network_identity
ON device_network_bindings(network_identity_id)
WHERE current = 1;

CREATE UNIQUE INDEX uq_device_network_bindings_current_installation
ON device_network_bindings(installation_id)
WHERE current = 1;

CREATE INDEX ix_device_network_bindings_last_connected
ON device_network_bindings(last_connected_at_utc);
