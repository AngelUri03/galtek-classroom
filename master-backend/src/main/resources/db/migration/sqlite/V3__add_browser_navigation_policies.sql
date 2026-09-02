-- Browser navigation policy source of truth for the Master.
-- This models administrative policy only; it does not apply browser lockdown.

CREATE TABLE browser_access_policies (
    policy_id TEXT PRIMARY KEY,
    classroom_id TEXT NOT NULL,
    name TEXT NOT NULL,
    mode TEXT NOT NULL,
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
    CHECK (mode IN ('UNRESTRICTED', 'BLOCKLIST', 'ALLOWLIST')),
    CHECK (scope_type IN ('CLASSROOM', 'GROUP', 'DEVICE')),
    CHECK (account_scope IN ('ANY', 'PRIMARY', 'SECONDARY')),
    CHECK (
        (scope_type = 'CLASSROOM' AND school_group_id IS NULL AND device_id IS NULL)
        OR (scope_type = 'GROUP' AND school_group_id IS NOT NULL AND device_id IS NULL)
        OR (scope_type = 'DEVICE' AND school_group_id IS NULL AND device_id IS NOT NULL)
    )
);

CREATE TABLE browser_url_rules (
    rule_id TEXT PRIMARY KEY,
    policy_id TEXT NOT NULL,
    action TEXT NOT NULL,
    match_type TEXT NOT NULL,
    pattern TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    description TEXT,
    version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (policy_id) REFERENCES browser_access_policies(policy_id) ON DELETE RESTRICT,
    CHECK (action IN ('ALLOW', 'BLOCK')),
    CHECK (match_type IN ('HOST_EXACT', 'HOST_SUFFIX', 'URL_PREFIX', 'EXACT_URL'))
);

CREATE UNIQUE INDEX uq_browser_policy_active_classroom_account
ON browser_access_policies(classroom_id, account_scope)
WHERE active = 1 AND scope_type = 'CLASSROOM';

CREATE UNIQUE INDEX uq_browser_policy_active_group_account
ON browser_access_policies(classroom_id, school_group_id, account_scope)
WHERE active = 1 AND scope_type = 'GROUP';

CREATE UNIQUE INDEX uq_browser_policy_active_device_account
ON browser_access_policies(classroom_id, device_id, account_scope)
WHERE active = 1 AND scope_type = 'DEVICE';

CREATE INDEX ix_browser_access_policies_classroom
ON browser_access_policies(classroom_id);

CREATE INDEX ix_browser_access_policies_group
ON browser_access_policies(school_group_id);

CREATE INDEX ix_browser_access_policies_device
ON browser_access_policies(device_id);

CREATE INDEX ix_browser_url_rules_policy
ON browser_url_rules(policy_id);
