-- Browser download policy source of truth for the Master.
-- This models administrative policy only; enforcement is planned for Prompt 16E2.
-- Arbitrary extension or MIME deny/allow lists are intentionally not modeled here:
-- Chrome and Edge on Windows do not currently share a supported native policy that
-- Galtek can enforce equivalently for arbitrary extension blocking.

CREATE TABLE browser_download_policies (
    policy_id TEXT PRIMARY KEY,
    classroom_id TEXT NOT NULL,
    name TEXT NOT NULL,
    restriction_mode TEXT NOT NULL,
    scope_type TEXT NOT NULL,
    school_group_id TEXT,
    device_id TEXT,
    account_scope TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (classroom_id) REFERENCES classrooms(classroom_id) ON DELETE RESTRICT,
    FOREIGN KEY (school_group_id) REFERENCES school_groups(group_id) ON DELETE RESTRICT,
    FOREIGN KEY (device_id) REFERENCES devices(device_id) ON DELETE RESTRICT,
    CHECK (restriction_mode IN (
        'NO_SPECIAL_RESTRICTIONS',
        'BLOCK_DANGEROUS',
        'BLOCK_POTENTIALLY_DANGEROUS',
        'BLOCK_ALL',
        'BLOCK_MALICIOUS'
    )),
    CHECK (scope_type IN ('CLASSROOM', 'GROUP', 'DEVICE')),
    CHECK (account_scope IN ('ANY', 'PRIMARY', 'SECONDARY')),
    CHECK (
        (scope_type = 'CLASSROOM' AND school_group_id IS NULL AND device_id IS NULL)
        OR (scope_type = 'GROUP' AND school_group_id IS NOT NULL AND device_id IS NULL)
        OR (scope_type = 'DEVICE' AND school_group_id IS NULL AND device_id IS NOT NULL)
    )
);

CREATE UNIQUE INDEX uq_browser_download_policy_active_classroom_account
ON browser_download_policies(classroom_id, account_scope)
WHERE active = 1 AND scope_type = 'CLASSROOM';

CREATE UNIQUE INDEX uq_browser_download_policy_active_group_account
ON browser_download_policies(classroom_id, school_group_id, account_scope)
WHERE active = 1 AND scope_type = 'GROUP';

CREATE UNIQUE INDEX uq_browser_download_policy_active_device_account
ON browser_download_policies(classroom_id, device_id, account_scope)
WHERE active = 1 AND scope_type = 'DEVICE';

CREATE INDEX ix_browser_download_policies_classroom
ON browser_download_policies(classroom_id);

CREATE INDEX ix_browser_download_policies_group
ON browser_download_policies(school_group_id);

CREATE INDEX ix_browser_download_policies_device
ON browser_download_policies(device_id);

CREATE TRIGGER tr_browser_download_policy_group_classroom_insert
BEFORE INSERT ON browser_download_policies
WHEN NEW.scope_type = 'GROUP'
     AND NOT EXISTS (
         SELECT 1
         FROM school_groups
         WHERE group_id = NEW.school_group_id
           AND classroom_id = NEW.classroom_id
     )
BEGIN
    SELECT RAISE(ABORT, 'browser download policy group must belong to classroom');
END;

CREATE TRIGGER tr_browser_download_policy_group_classroom_update
BEFORE UPDATE OF classroom_id, scope_type, school_group_id ON browser_download_policies
WHEN NEW.scope_type = 'GROUP'
     AND NOT EXISTS (
         SELECT 1
         FROM school_groups
         WHERE group_id = NEW.school_group_id
           AND classroom_id = NEW.classroom_id
     )
BEGIN
    SELECT RAISE(ABORT, 'browser download policy group must belong to classroom');
END;

CREATE TRIGGER tr_browser_download_policy_device_classroom_insert
BEFORE INSERT ON browser_download_policies
WHEN NEW.scope_type = 'DEVICE'
     AND NOT EXISTS (
         SELECT 1
         FROM devices
         WHERE device_id = NEW.device_id
           AND classroom_id = NEW.classroom_id
     )
BEGIN
    SELECT RAISE(ABORT, 'browser download policy device must belong to classroom');
END;

CREATE TRIGGER tr_browser_download_policy_device_classroom_update
BEFORE UPDATE OF classroom_id, scope_type, device_id ON browser_download_policies
WHEN NEW.scope_type = 'DEVICE'
     AND NOT EXISTS (
         SELECT 1
         FROM devices
         WHERE device_id = NEW.device_id
           AND classroom_id = NEW.classroom_id
     )
BEGIN
    SELECT RAISE(ABORT, 'browser download policy device must belong to classroom');
END;
