-- Admin credentials, and the last settings that used to live in a file.
--
-- Same rule as 002: SchemaInitializer re-runs every script on every boot with no
-- version table, so everything here is idempotent and there is no ALTER TABLE.

-- One row. The username is stored so it can be changed too — an operator who
-- wants a shared account name other than "admin" should not have to edit a file,
-- because after this release there are no files.
CREATE TABLE IF NOT EXISTS admin_credential (
    id            INTEGER PRIMARY KEY CHECK (id = 1),
    username      TEXT    NOT NULL,
    password_hash TEXT    NOT NULL,   -- pbkdf2-sha256$<iterations>$<salt>$<hash>
    is_default    INTEGER NOT NULL DEFAULT 1,
    updated_utc   TEXT    NOT NULL
);

-- Deliberately NOT seeded here: the default password has to be hashed with a
-- random salt, which SQL cannot do. Program.cs seeds admin/password on first run
-- and sets is_default, which is what drives the warning banner.
