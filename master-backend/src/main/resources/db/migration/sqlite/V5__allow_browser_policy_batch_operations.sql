-- Prompt 16F1 allows the existing BatchOperation table to persist browser
-- policy apply batches. No new operation table is introduced.

ALTER TABLE batch_target_results RENAME TO batch_target_results_old;
ALTER TABLE batch_operations RENAME TO batch_operations_old;

CREATE TABLE batch_operations (
    operation_id TEXT PRIMARY KEY,
    classroom_id TEXT NOT NULL,
    operation_type TEXT NOT NULL,
    requested_by TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    started_at_utc TEXT,
    completed_at_utc TEXT,
    status TEXT NOT NULL,
    target_count INTEGER NOT NULL CHECK (target_count >= 0),
    payload_schema_version INTEGER,
    payload_json TEXT,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    FOREIGN KEY (classroom_id) REFERENCES classrooms(classroom_id) ON DELETE RESTRICT,
    CHECK (operation_type IN (
        'LOCK_INPUT', 'UNLOCK_INPUT', 'SHUTDOWN', 'RESTART', 'OPEN_APPLICATION',
        'OPEN_URL', 'START_PROJECTION', 'STOP_PROJECTION', 'DISTRIBUTE_FILE',
        'CREATE_FOLDER', 'SET_WALLPAPER', 'RESTORE_WALLPAPER',
        'GET_WINDOWS_SESSION_STATE', 'LOGON_MANAGED_ACCOUNT',
        'LOGOFF_WINDOWS_SESSION', 'SWITCH_MANAGED_ACCOUNT',
        'APPLY_BROWSER_NAVIGATION_POLICY', 'APPLY_BROWSER_DOWNLOAD_POLICY',
        'ASSIGN_STUDENT', 'MOVE_STUDENT', 'SWAP_STUDENTS',
        'SYNC_STUDENT_WORKSPACE', 'RESTORE_STUDENT_WORKSPACE'
    )),
    CHECK (status IN (
        'PLANNED', 'PREFLIGHT', 'RUNNING', 'SUCCESS', 'PARTIAL_SUCCESS',
        'FAILED', 'CANCELLED', 'ROLLING_BACK', 'ROLLED_BACK'
    ))
);

CREATE TABLE batch_target_results (
    operation_id TEXT NOT NULL,
    target_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    target_display_name TEXT NOT NULL,
    status TEXT NOT NULL,
    error_code TEXT,
    message TEXT,
    attempt INTEGER NOT NULL CHECK (attempt >= 1),
    updated_at_utc TEXT NOT NULL,
    PRIMARY KEY (operation_id, target_type, target_id),
    FOREIGN KEY (operation_id) REFERENCES batch_operations(operation_id) ON DELETE RESTRICT,
    CHECK (target_type IN ('CLASSROOM', 'GROUP', 'STUDENT', 'DEVICE')),
    CHECK (status IN ('PENDING', 'SUCCESS', 'FAILED', 'SKIPPED', 'CANCELLED', 'ROLLED_BACK')),
    CHECK (status <> 'FAILED' OR error_code IS NOT NULL)
);

INSERT INTO batch_operations (
    operation_id, classroom_id, operation_type, requested_by, created_at_utc,
    started_at_utc, completed_at_utc, status, target_count,
    payload_schema_version, payload_json, version
)
SELECT operation_id, classroom_id, operation_type, requested_by, created_at_utc,
       started_at_utc, completed_at_utc, status, target_count,
       payload_schema_version, payload_json, version
FROM batch_operations_old;

INSERT INTO batch_target_results (
    operation_id, target_type, target_id, target_display_name,
    status, error_code, message, attempt, updated_at_utc
)
SELECT operation_id, target_type, target_id, target_display_name,
       status, error_code, message, attempt, updated_at_utc
FROM batch_target_results_old;

DROP TABLE batch_target_results_old;
DROP TABLE batch_operations_old;

CREATE INDEX ix_batch_operations_created_status ON batch_operations(created_at_utc, status);
CREATE INDEX ix_batch_target_results_operation_status ON batch_target_results(operation_id, status);
