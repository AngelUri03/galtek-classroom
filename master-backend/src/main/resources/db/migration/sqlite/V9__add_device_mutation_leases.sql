CREATE TABLE device_mutation_leases (
    device_id TEXT PRIMARY KEY,
    state TEXT NOT NULL,
    operation_type TEXT NOT NULL,
    target_profile TEXT,
    started_at_utc TEXT NOT NULL,
    CHECK (state IN ('IN_PROGRESS', 'RECONCILIATION_REQUIRED'))
);

CREATE INDEX ix_device_mutation_leases_state
ON device_mutation_leases(state, started_at_utc);
