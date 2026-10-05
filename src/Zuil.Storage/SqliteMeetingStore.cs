using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zuil.Core.Abstractions;
using Zuil.Core.Models;

namespace Zuil.Storage;

/// <summary>
/// The kiosk's read model. Single-writer (the sync worker) and many readers (HTTP
/// requests), which is exactly what SQLite in WAL mode is good at.
/// </summary>
public sealed class SqliteMeetingStore : IMeetingStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<SqliteMeetingStore> _logger;

    // Serializes writes. SQLite would serialize them anyway; doing it here turns
    // lock contention into an await instead of a SQLITE_BUSY exception.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteMeetingStore(
        IOptions<StorageOptions> options,
        ILogger<SqliteMeetingStore> logger)
    {
        _logger = logger;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = options.Value.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        };

        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();

        using (var pragma = _connection.CreateCommand())
        {
            // WAL so a long sync write never blocks a visitor's search.
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        SchemaInitializer.Apply(_connection, logger);
    }

    public async Task ReplaceWindowAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyList<Meeting> meetings,
        CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            // One transaction for the whole swap: readers see either the old
            // window or the new one, never a partially rebuilt cache.
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(ct);

            await ExecuteAsync(tx, "DELETE FROM attendee;", ct);
            await ExecuteAsync(tx, "DELETE FROM meeting;", ct);

            foreach (var meeting in meetings)
            {
                await using var insert = _connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO meeting (id, room_id, subject, start_utc, end_utc, is_cancelled)
                    VALUES ($id, $room, $subject, $start, $end, $cancelled);
                    """;
                insert.Parameters.AddWithValue("$id", meeting.Id);
                insert.Parameters.AddWithValue("$room", meeting.RoomId);
                insert.Parameters.AddWithValue("$subject", (object?)meeting.Subject ?? DBNull.Value);
                insert.Parameters.AddWithValue("$start", Iso(meeting.Start));
                insert.Parameters.AddWithValue("$end", Iso(meeting.End));
                insert.Parameters.AddWithValue("$cancelled", meeting.IsCancelled ? 1 : 0);
                await insert.ExecuteNonQueryAsync(ct);

                foreach (var attendee in meeting.Attendees)
                {
                    await using var ins = _connection.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = """
                        INSERT INTO attendee (room_id, meeting_id, name, normalized_name)
                        VALUES ($room, $meeting, $name, $normalized);
                        """;
                    ins.Parameters.AddWithValue("$room", meeting.RoomId);
                    ins.Parameters.AddWithValue("$meeting", meeting.Id);
                    ins.Parameters.AddWithValue("$name", attendee.Name);
                    ins.Parameters.AddWithValue("$normalized", attendee.NormalizedName);
                    await ins.ExecuteNonQueryAsync(ct);
                }
            }

            await using (var state = _connection.CreateCommand())
            {
                state.Transaction = tx;
                state.CommandText = """
                    UPDATE sync_state
                       SET last_success_utc = $now,
                           last_attempt_utc = $now,
                           last_error       = NULL,
                           window_start_utc = $start,
                           window_end_utc   = $end
                     WHERE id = 1;
                    """;
                state.Parameters.AddWithValue("$now", Iso(DateTimeOffset.UtcNow));
                state.Parameters.AddWithValue("$start", Iso(windowStart));
                state.Parameters.AddWithValue("$end", Iso(windowEnd));
                await state.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            _logger.LogInformation("Cache replaced with {Count} occurrences", meetings.Count);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<Meeting>> FindByAttendeeAsync(
        string normalizedName,
        DateTimeOffset now,
        TimeSpan lookahead,
        CancellationToken ct)
    {
        // Equality on normalized_name, never LIKE: a prefix search would turn the
        // kiosk into a directory of everyone in the building.
        const string sql = """
            SELECT m.id, m.room_id, m.subject, m.start_utc, m.end_utc, m.is_cancelled
              FROM meeting m
              JOIN attendee a ON a.room_id = m.room_id AND a.meeting_id = m.id
             WHERE a.normalized_name = $name
               AND m.is_cancelled = 0
               AND m.end_utc   > $now
               AND m.start_utc < $until
             ORDER BY m.start_utc
             LIMIT 10;
            """;

        return await QueryMeetingsAsync(sql, ct, cmd =>
        {
            cmd.Parameters.AddWithValue("$name", normalizedName);
            cmd.Parameters.AddWithValue("$now", Iso(now));
            cmd.Parameters.AddWithValue("$until", Iso(now + lookahead));
        });
    }

    public async Task<IReadOnlyList<Meeting>> GetByRoomAsync(
        string roomId,
        DateTimeOffset now,
        TimeSpan lookahead,
        CancellationToken ct)
    {
        const string sql = """
            SELECT id, room_id, subject, start_utc, end_utc, is_cancelled
              FROM meeting
             WHERE room_id = $room
               AND is_cancelled = 0
               AND end_utc   > $now
               AND start_utc < $until
             ORDER BY start_utc;
            """;

        return await QueryMeetingsAsync(sql, ct, cmd =>
        {
            cmd.Parameters.AddWithValue("$room", roomId);
            cmd.Parameters.AddWithValue("$now", Iso(now));
            cmd.Parameters.AddWithValue("$until", Iso(now + lookahead));
        });
    }

    public async Task<SyncStatus> GetSyncStatusAsync(CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.last_success_utc,
                   s.last_attempt_utc,
                   s.last_error,
                   (SELECT COUNT(*) FROM meeting)
              FROM sync_state s
             WHERE s.id = 1;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new SyncStatus();
        }

        return new SyncStatus
        {
            LastSuccessfulSync = reader.IsDBNull(0) ? null : Parse(reader.GetString(0)),
            LastAttemptedSync = reader.IsDBNull(1) ? null : Parse(reader.GetString(1)),
            LastError = reader.IsDBNull(2) ? null : reader.GetString(2),
            MeetingCount = reader.GetInt32(3),
        };
    }

    public async Task RecordSyncAttemptAsync(DateTimeOffset at, string? error, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE sync_state
                   SET last_attempt_utc = $at,
                       last_error       = $error
                 WHERE id = 1;
                """;
            cmd.Parameters.AddWithValue("$at", Iso(at));
            cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task PurgeBeforeAsync(DateTimeOffset before, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM meeting WHERE end_utc < $before;";
            cmd.Parameters.AddWithValue("$before", Iso(before));
            var removed = await cmd.ExecuteNonQueryAsync(ct);

            if (removed > 0)
            {
                _logger.LogDebug("Purged {Count} finished occurrences", removed);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<IReadOnlyList<Meeting>> QueryMeetingsAsync(
        string sql,
        CancellationToken ct,
        Action<SqliteCommand> bind)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        bind(cmd);

        var meetings = new List<Meeting>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            meetings.Add(new Meeting
            {
                Id = reader.GetString(0),
                RoomId = reader.GetString(1),
                Subject = reader.IsDBNull(2) ? null : reader.GetString(2),
                Start = Parse(reader.GetString(3)),
                End = Parse(reader.GetString(4)),
                IsCancelled = reader.GetInt32(5) != 0,
                // Attendees are intentionally not hydrated on read: nothing the
                // kiosk displays needs them, and not loading them means they
                // cannot leak into an API response by accident.
                Attendees = [],
            });
        }

        return meetings;
    }

    private async Task ExecuteAsync(SqliteTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _writeLock.Dispose();
        _connection.Dispose();
    }
}
