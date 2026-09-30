-- 20F.2-F1 preserves the difference between a failed target, a session switch
-- that closed the source but could not start the target, and an operation whose
-- final outcome could not be confirmed.

ALTER TABLE batch_target_results RENAME TO batch_target_results_old;

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
    CHECK (status IN (
        'PENDING', 'SUCCESS', 'NO_CHANGE', 'PARTIAL', 'UNKNOWN',
        'FAILED', 'SKIPPED', 'CANCELLED', 'ROLLED_BACK'
    )),
    CHECK (status NOT IN ('FAILED', 'PARTIAL', 'UNKNOWN') OR error_code IS NOT NULL)
);

INSERT INTO batch_target_results (
    operation_id, target_type, target_id, target_display_name,
    status, error_code, message, attempt, updated_at_utc
)
SELECT operation_id, target_type, target_id, target_display_name,
       status, error_code, message, attempt, updated_at_utc
FROM batch_target_results_old;

DROP TABLE batch_target_results_old;

CREATE INDEX ix_batch_target_results_operation_status ON batch_target_results(operation_id, status);
