-- Cache of the synced calendar window. Replaced wholesale on every successful
-- sync, so there is no incremental-merge logic to get wrong.

CREATE TABLE IF NOT EXISTS meeting (
    id            TEXT    NOT NULL,
    room_id       TEXT    NOT NULL,
    subject       TEXT    NULL,
    start_utc     TEXT    NOT NULL,   -- ISO-8601, always UTC
    end_utc       TEXT    NOT NULL,
    is_cancelled  INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (room_id, id)
);

CREATE INDEX IF NOT EXISTS ix_meeting_end ON meeting (end_utc);
CREATE INDEX IF NOT EXISTS ix_meeting_room_start ON meeting (room_id, start_utc);

-- Attendees are a separate table purely so the name lookup can be an indexed
-- equality probe rather than a scan over a serialized blob.
CREATE TABLE IF NOT EXISTS attendee (
    room_id         TEXT NOT NULL,
    meeting_id      TEXT NOT NULL,
    name            TEXT NOT NULL,
    normalized_name TEXT NOT NULL,
    FOREIGN KEY (room_id, meeting_id) REFERENCES meeting (room_id, id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_attendee_normalized ON attendee (normalized_name);

-- Single-row table tracking sync health, so the kiosk can warn when its data is
-- stale instead of silently showing yesterday's meetings.
CREATE TABLE IF NOT EXISTS sync_state (
    id                 INTEGER PRIMARY KEY CHECK (id = 1),
    last_success_utc   TEXT NULL,
    last_attempt_utc   TEXT NULL,
    last_error         TEXT NULL,
    window_start_utc   TEXT NULL,
    window_end_utc     TEXT NULL
);

INSERT OR IGNORE INTO sync_state (id) VALUES (1);
