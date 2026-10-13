-- Everything the admin GUI edits, plus the rooms it edits them for.
--
-- SchemaInitializer re-runs every script on every boot and there is no version
-- table, so every statement in here has to be idempotent. In particular: no
-- ALTER TABLE ADD COLUMN, which throws the second time. If an additive column is
-- ever needed, use SchemaInitializer.EnsureColumn, which checks pragma_table_info
-- first.

-- Key/value rather than a column per setting: the kiosk will gain settings over
-- time, and a wide table means a migration per setting with no version table to
-- hang the ordering on. Keys use configuration-style colon notation.
CREATE TABLE IF NOT EXISTS setting (
    key         TEXT    NOT NULL PRIMARY KEY,
    value       TEXT    NULL,       -- scalar, TimeSpan as "c", or an encrypted payload
    is_secret   INTEGER NOT NULL DEFAULT 0,
    updated_utc TEXT    NOT NULL
);

-- Rooms move out of config/rooms.json so staff can change a looproute without
-- SSH-ing into a Pi bolted to a wall. rooms.json stays as the first-run seed.
CREATE TABLE IF NOT EXISTS room (
    id           TEXT    NOT NULL PRIMARY KEY,
    mailbox      TEXT    NOT NULL,
    display_name TEXT    NOT NULL,
    floor        TEXT    NOT NULL,
    directions   TEXT    NOT NULL,
    signal_id    INTEGER NOT NULL,
    sort_order   INTEGER NOT NULL DEFAULT 0,
    is_enabled   INTEGER NOT NULL DEFAULT 1,
    updated_utc  TEXT    NOT NULL
);

-- Two rooms sharing an indicator means the column points at the wrong place half
-- the time. SettingsValidator rejects it on write; these indexes mean a
-- hand-edited database cannot get past it either.
CREATE UNIQUE INDEX IF NOT EXISTS ux_room_signal  ON room (signal_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_room_mailbox ON room (mailbox);

-- The agenda views order across every room at once, which ix_meeting_room_start
-- cannot serve: its leading column is room_id.
CREATE INDEX IF NOT EXISTS ix_meeting_start ON meeting (start_utc);

-- Failed-login ledger. An in-memory counter is defeated by pulling the plug,
-- which is the obvious attack on a kiosk whose password defaults to 12345678.
CREATE TABLE IF NOT EXISTS admin_login_attempt (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    at_utc    TEXT    NOT NULL,
    succeeded INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_admin_attempt_at ON admin_login_attempt (at_utc);

-- Marker so seeding from config/rooms.json happens exactly once. An
-- administrator who deletes every room must not find them back after a restart.
INSERT OR IGNORE INTO setting (key, value, is_secret, updated_utc)
VALUES ('Schema:Seeded', '0', 0, '1970-01-01T00:00:00.0000000Z');
