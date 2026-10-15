using Microsoft.Extensions.Logging.Abstractions;
using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Storage.Tests;

/// <summary>
/// SchemaInitializer re-runs every embedded script on every boot and there is no
/// version table, so idempotence is not a nicety — a script that throws the
/// second time means a kiosk that boots once and then never again.
/// </summary>
public class MigrationTests
{
    [Fact]
    public void Applying_twice_does_not_throw()
    {
        using var db = new TestDatabase();

        // Once in the ZuilDatabase constructor, once here.
        SchemaInitializer.Apply(db.Database.Connection, NullLogger.Instance);
        SchemaInitializer.Apply(db.Database.Connection, NullLogger.Instance);
    }

    [Fact]
    public async Task Applying_twice_keeps_the_data()
    {
        using var db = new TestDatabase();

        await db.Settings.SaveRoomsAsync(
            [
                new Room
                {
                    Id = "zaal-1-01",
                    Mailbox = "zaal-1-01@example.com",
                    DisplayName = "Vergaderzaal 1.01",
                    Floor = "1e verdieping",
                    Directions = "Trap op.",
                    SignalId = 1,
                },
            ],
            CancellationToken.None);

        await db.Settings.SaveAsync(
            new KioskSettings { Sync = new SyncSettings { Interval = TimeSpan.FromMinutes(9) } },
            CancellationToken.None);
        await db.Settings.SetGraphSecretAsync("the-secret", CancellationToken.None);

        SchemaInitializer.Apply(db.Database.Connection, NullLogger.Instance);

        var loaded = await db.Settings.LoadAsync(CancellationToken.None);

        Assert.Single(loaded.Rooms);
        Assert.Equal(TimeSpan.FromMinutes(9), loaded.Sync.Interval);
        Assert.True(await db.Settings.HasGraphSecretAsync(CancellationToken.None));
    }

    /// <summary>
    /// The seeded marker must not be reset by a later boot, or an administrator
    /// who deleted every room would find them back from the built-in defaults.
    /// </summary>
    [Fact]
    public async Task Re_applying_does_not_reset_the_seeded_marker()
    {
        using var db = new TestDatabase();

        await db.Settings.MarkSeededAsync(CancellationToken.None);

        SchemaInitializer.Apply(db.Database.Connection, NullLogger.Instance);

        Assert.True(await db.Settings.IsSeededAsync(CancellationToken.None));
    }

    /// <summary>
    /// The database is the last line of defence on the indicator invariant, for
    /// the case where someone edits it with sqlite3 rather than the GUI.
    /// </summary>
    [Fact]
    public void Database_refuses_two_rooms_on_one_indicator()
    {
        using var db = new TestDatabase();

        Insert(db, "a", 1);

        var ex = Assert.ThrowsAny<Exception>(() => Insert(db, "b", 1));
        Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Database_refuses_two_rooms_on_one_mailbox()
    {
        using var db = new TestDatabase();

        Insert(db, "a", 1, "shared@example.com");

        var ex = Assert.ThrowsAny<Exception>(() => Insert(db, "b", 2, "shared@example.com"));
        Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void Insert(TestDatabase db, string id, int signalId, string? mailbox = null)
    {
        using var cmd = db.Database.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO room
                (id, mailbox, display_name, floor, directions, signal_id, sort_order,
                 is_enabled, updated_utc)
            VALUES ($id, $mailbox, $id, 'x', 'y', $signal, 0, 1, '2026-01-01T00:00:00.0000000Z');
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$mailbox", mailbox ?? $"{id}@example.com");
        cmd.Parameters.AddWithValue("$signal", signalId);
        cmd.ExecuteNonQuery();
    }
}
