-- Galtek Classroom Master local domain schema.
-- Timestamps are TEXT UTC ISO-8601 values produced from Instant.toString().
-- MasterWindowsBinding is intentionally not stored here; Agent Service remains
-- the future authority for the Windows SID binding.

CREATE TABLE classrooms (
    classroom_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    default_browser_profile_id TEXT,
    workspace_recovery_planned INTEGER NOT NULL DEFAULT 1 CHECK (workspace_recovery_planned IN (0, 1)),
    batch_confirmations_required INTEGER NOT NULL DEFAULT 1 CHECK (batch_confirmations_required IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0)
);

CREATE TABLE application_definitions (
    application_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    type TEXT NOT NULL,
    availability TEXT NOT NULL,
    launch_policy TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    CHECK (type IN ('DESKTOP_APPLICATION', 'BROWSER', 'SYSTEM_UTILITY', 'EDUCATIONAL_CONTENT')),
    CHECK (availability IN ('REQUIRED', 'OPTIONAL', 'UNKNOWN', 'NOT_INSTALLED')),
    CHECK (launch_policy IN ('ALLOWED', 'BLOCKED', 'REQUIRES_SESSION', 'MASTER_ONLY'))
);

CREATE TABLE classroom_applications (
    classroom_id TEXT NOT NULL,
    application_id TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    PRIMARY KEY (classroom_id, application_id),
    FOREIGN KEY (classroom_id) REFERENCES classrooms(classroom_id) ON DELETE RESTRICT,
    FOREIGN KEY (application_id) REFERENCES application_definitions(application_id) ON DELETE RESTRICT
);

CREATE TABLE school_groups (
    group_id TEXT PRIMARY KEY,
    classroom_id TEXT NOT NULL,
    grade TEXT NOT NULL,
    section TEXT NOT NULL,
    display_name TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    FOREIGN KEY (classroom_id) REFERENCES classrooms(classroom_id) ON DELETE RESTRICT
);

CREATE TABLE students (
    student_id TEXT PRIMARY KEY,
    classroom_id TEXT NOT NULL,
    school_group_id TEXT NOT NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    display_name TEXT NOT NULL,
    grade TEXT NOT NULL,
    group_name TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    workspace_id TEXT NOT NULL,
    browser_profile_id TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    FOREIGN KEY (classroom_id) REFERENCES classrooms(classroom_id) ON DELETE RESTRICT,
    FOREIGN KEY (school_group_id) REFERENCES school_groups(group_id) ON DELETE RESTRICT
);

CREATE TABLE devices (
    device_id TEXT PRIMARY KEY,
    classroom_id TEXT NOT NULL,
    installation_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    hostname TEXT NOT NULL,
    status TEXT NOT NULL,
    last_seen_utc TEXT NOT NULL,
    capabilities_json TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    FOREIGN KEY (classroom_id) REFERENCES classrooms(classroom_id) ON DELETE RESTRICT,
    CHECK (status IN (
        'ONLINE', 'OFFLINE', 'CONNECTING', 'UNLICENSED', 'LICENSE_BLOCKED',
        'AGENT_UNAVAILABLE', 'SESSION_UNAVAILABLE', 'BUSY', 'ERROR'
    ))
);

CREATE UNIQUE INDEX uq_devices_installation_id ON devices(installation_id);

CREATE TABLE student_workspaces (
    workspace_id TEXT PRIMARY KEY,
    student_id TEXT NOT NULL UNIQUE,
    status TEXT NOT NULL,
    allowed_destinations_json TEXT NOT NULL,
    browser_profile_id TEXT NOT NULL,
    recovery_lightweight_history_planned INTEGER NOT NULL CHECK (recovery_lightweight_history_planned IN (0, 1)),
    recovery_controlled_trash_planned INTEGER NOT NULL CHECK (recovery_controlled_trash_planned IN (0, 1)),
    recovery_limited_snapshots_planned INTEGER NOT NULL CHECK (recovery_limited_snapshots_planned IN (0, 1)),
    recovery_restore_last_valid_state_planned INTEGER NOT NULL CHECK (recovery_restore_last_valid_state_planned IN (0, 1)),
    recovery_recover_teacher_distributed_files_planned INTEGER NOT NULL CHECK (recovery_recover_teacher_distributed_files_planned IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    FOREIGN KEY (student_id) REFERENCES students(student_id) ON DELETE RESTRICT,
    CHECK (status IN ('NOT_CREATED', 'PREPARING', 'READY', 'BUSY', 'RECOVERY_REQUIRED', 'ERROR'))
);

CREATE TABLE browser_profiles (
    browser_profile_id TEXT PRIMARY KEY,
    student_id TEXT NOT NULL UNIQUE,
    browser_type TEXT NOT NULL,
    display_name TEXT NOT NULL,
    profile_strategy TEXT NOT NULL,
    profile_reference TEXT NOT NULL,
    status TEXT NOT NULL,
    portability TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    FOREIGN KEY (student_id) REFERENCES students(student_id) ON DELETE RESTRICT,
    CHECK (browser_type IN ('CHROME', 'EDGE', 'OTHER_MANAGED')),
    CHECK (profile_strategy IN ('SYNCED_ACCOUNT', 'MANAGED_PROFILE', 'LOCAL_PROFILE')),
    CHECK (status IN ('READY', 'NOT_CONFIGURED', 'UNAVAILABLE', 'NOT_PORTABLE', 'REAUTH_REQUIRED', 'ERROR')),
    CHECK (portability IN ('PORTABLE', 'NOT_PORTABLE', 'REAUTH_REQUIRED', 'UNKNOWN'))
);

CREATE TABLE master_browser_profiles (
    master_browser_profile_id TEXT PRIMARY KEY,
    browser_type TEXT NOT NULL,
    display_name TEXT NOT NULL,
    profile_strategy TEXT NOT NULL,
    profile_reference TEXT NOT NULL,
    owner_windows_sid TEXT,
    status TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    default_profile INTEGER NOT NULL DEFAULT 0 CHECK (default_profile IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    CHECK (browser_type IN ('CHROME', 'EDGE', 'OTHER_MANAGED')),
    CHECK (profile_strategy IN ('SYNCED_ACCOUNT', 'MANAGED_PROFILE', 'LOCAL_PROFILE')),
    CHECK (status IN ('READY', 'NOT_CONFIGURED', 'UNAVAILABLE', 'NOT_PORTABLE', 'REAUTH_REQUIRED', 'ERROR'))
);

CREATE UNIQUE INDEX uq_master_browser_profiles_default
ON master_browser_profiles(browser_type)
WHERE default_profile = 1 AND active = 1;

CREATE TABLE device_assignments (
    assignment_id TEXT PRIMARY KEY,
    student_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    assigned_at_utc TEXT NOT NULL,
    ended_at_utc TEXT,
    status TEXT NOT NULL,
    source TEXT NOT NULL,
    current INTEGER NOT NULL CHECK (current IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    FOREIGN KEY (student_id) REFERENCES students(student_id) ON DELETE RESTRICT,
    FOREIGN KEY (device_id) REFERENCES devices(device_id) ON DELETE RESTRICT,
    CHECK (status IN ('PLANNED', 'CURRENT', 'RELEASING', 'ENDED', 'CANCELLED')),
    CHECK (source IN ('MANUAL', 'IMPORTED', 'AUTOMATIC', 'MOVE_WORKFLOW', 'RECOVERY')),
    CHECK (NOT (current = 1 AND status = 'ENDED')),
    CHECK ((ended_at_utc IS NULL AND current = 1) OR current = 0)
);

CREATE UNIQUE INDEX uq_device_assignments_current_student
ON device_assignments(student_id)
WHERE current = 1;

CREATE UNIQUE INDEX uq_device_assignments_current_device
ON device_assignments(device_id)
WHERE current = 1;

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
        'CREATE_FOLDER', 'SET_WALLPAPER', 'RESTORE_WALLPAPER', 'ASSIGN_STUDENT',
        'MOVE_STUDENT', 'SWAP_STUDENTS', 'SYNC_STUDENT_WORKSPACE',
        'RESTORE_STUDENT_WORKSPACE'
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

CREATE INDEX ix_school_groups_classroom ON school_groups(classroom_id);
CREATE INDEX ix_students_group ON students(school_group_id);
CREATE INDEX ix_students_active_classroom ON students(classroom_id, active);
CREATE INDEX ix_devices_classroom ON devices(classroom_id);
CREATE INDEX ix_device_assignments_current ON device_assignments(current);
CREATE INDEX ix_device_assignments_student ON device_assignments(student_id);
CREATE INDEX ix_device_assignments_device ON device_assignments(device_id);
CREATE INDEX ix_student_workspaces_student ON student_workspaces(student_id);
CREATE INDEX ix_browser_profiles_student ON browser_profiles(student_id);
CREATE INDEX ix_classroom_applications_application ON classroom_applications(application_id);
CREATE INDEX ix_batch_operations_created_status ON batch_operations(created_at_utc, status);
CREATE INDEX ix_batch_target_results_operation_status ON batch_target_results(operation_id, status);
