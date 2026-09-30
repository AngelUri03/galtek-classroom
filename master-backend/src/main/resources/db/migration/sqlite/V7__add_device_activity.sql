CREATE TABLE device_activity_events (
    event_id TEXT PRIMARY KEY,
    classroom_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    event_type TEXT NOT NULL,
    actor TEXT NOT NULL,
    occurred_at_utc TEXT NOT NULL,
    role TEXT,
    account_reference TEXT,
    result TEXT NOT NULL,
    message TEXT,
    FOREIGN KEY (classroom_id) REFERENCES classrooms(classroom_id),
    FOREIGN KEY (device_id) REFERENCES devices(device_id)
);

CREATE INDEX idx_device_activity_lookup
    ON device_activity_events(classroom_id, device_id, occurred_at_utc DESC);
