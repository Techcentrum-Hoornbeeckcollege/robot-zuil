using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Storage.Tests;

public class SqliteSettingsStoreTests
{
    /// <summary>
    /// A fresh install has every key missing and still has to boot, so loading
    /// an empty table must give the defaults rather than throw.
    /// </summary>
    [Fact]
    public async Task Loads_defaults_from_an_empty_table()
    {
        using var db = new TestDatabase();

        var settings = await db.Settings.LoadAsync(CancellationToken.None);

        Assert.Equal(new SyncSettings(), settings.Sync);
        Assert.Equal(new DeviceSettings(), settings.Device);
        Assert.Equal(SubjectVisibility.Hidden, settings.Privacy.SubjectVisibility);
        Assert.Equal(AttractView.Upcoming, settings.Ui.AttractView);
        Assert.Empty(settings.Rooms);
    }

    [Fact]
    public async Task Round_trips_every_setting()
    {
        using var db = new TestDatabase();

        var saved = new KioskSettings
        {
            Graph = new GraphSettings
            {
                TenantId = "11111111-1111-1111-1111-111111111111",
                ClientId = "22222222-2222-2222-2222-222222222222",
                BaseUrl = "https://graph.microsoft.com/beta",
                TimeZone = "Europe/Brussels",
            },
            Sync = new SyncSettings
            {
                Interval = TimeSpan.FromMinutes(7),
                WindowBehind = TimeSpan.FromHours(3),
                WindowAhead = TimeSpan.FromHours(48),
                StaleAfter = TimeSpan.FromMinutes(21),
                RetainPastMeetingsFor = TimeSpan.FromHours(5),
            },
            Device = new DeviceSettings
            {
                BusId = 2,
                Address = 0x50,
                AttentionPin = 22,
                ResponseDelay = TimeSpan.FromMilliseconds(9),
                MaxRetries = 5,
                MaxCommandsPerSecond = 25,
            },
            Privacy = new PrivacySettings
            {
                SubjectVisibility = SubjectVisibility.AdminOnly,
                IdleTimeout = TimeSpan.FromSeconds(90),
            },
            Ui = new UiSettings
            {
                AttractView = AttractView.Board,
                UpcomingCount = 12,
                ShowAgendaNav = false,
            },
        };

        await db.Settings.SaveAsync(saved, CancellationToken.None);
        var loaded = await db.Settings.LoadAsync(CancellationToken.None);

        Assert.Equal(saved.Graph.TenantId, loaded.Graph.TenantId);
        Assert.Equal(saved.Graph.ClientId, loaded.Graph.ClientId);
        Assert.Equal(saved.Graph.BaseUrl, loaded.Graph.BaseUrl);
        Assert.Equal(saved.Graph.TimeZone, loaded.Graph.TimeZone);
        Assert.Equal(saved.Sync, loaded.Sync);
        Assert.Equal(saved.Device, loaded.Device);
        Assert.Equal(saved.Privacy, loaded.Privacy);
        Assert.Equal(saved.Ui, loaded.Ui);
    }

    /// <summary>
    /// An attention pin of null means "no pin", which has to survive a round
    /// trip differently from the key being absent.
    /// </summary>
    [Fact]
    public async Task Round_trips_a_disabled_attention_pin()
    {
        using var db = new TestDatabase();

        await db.Settings.SaveAsync(
            new KioskSettings { Device = new DeviceSettings { AttentionPin = null } },
            CancellationToken.None);

        var loaded = await db.Settings.LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Device.AttentionPin);
    }

    /// <summary>
    /// SaveAsync must not be able to clear the credential. The settings object
    /// has no secret on it, and that is what makes a round trip of the admin
    /// form safe.
    /// </summary>
    [Fact]
    public async Task Saving_settings_leaves_the_secret_alone()
    {
        using var db = new TestDatabase();

        await db.Settings.SetGraphSecretAsync("the-secret", CancellationToken.None);
        await db.Settings.SaveAsync(new KioskSettings(), CancellationToken.None);

        Assert.True(await db.Settings.HasGraphSecretAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reports_whether_a_secret_is_stored()
    {
        using var db = new TestDatabase();

        Assert.False(await db.Settings.HasGraphSecretAsync(CancellationToken.None));

        await db.Settings.SetGraphSecretAsync("the-secret", CancellationToken.None);
        Assert.True(await db.Settings.HasGraphSecretAsync(CancellationToken.None));

        await db.Settings.SetGraphSecretAsync(null, CancellationToken.None);
        Assert.False(await db.Settings.HasGraphSecretAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Credentials_are_incomplete_until_everything_is_set()
    {
        using var db = new TestDatabase();

        Assert.Null(await db.Settings.GetAsync(CancellationToken.None));

        await db.Settings.SaveAsync(
            new KioskSettings
            {
                Graph = new GraphSettings
                {
                    TenantId = "11111111-1111-1111-1111-111111111111",
                    ClientId = "22222222-2222-2222-2222-222222222222",
                },
            },
            CancellationToken.None);

        // Tenant and client but no secret is still not enough to ask for a token.
        Assert.Null(await db.Settings.GetAsync(CancellationToken.None));

        await db.Settings.SetGraphSecretAsync("the-secret", CancellationToken.None);

        var credentials = await db.Settings.GetAsync(CancellationToken.None);
        Assert.NotNull(credentials);
        Assert.True(credentials.IsComplete);
        Assert.Equal("the-secret", credentials.ClientSecret);
    }

    [Fact]
    public async Task Replaces_the_room_set_wholesale()
    {
        using var db = new TestDatabase();

        await db.Settings.SaveRoomsAsync([Room("a", 1), Room("b", 2)], CancellationToken.None);
        await db.Settings.SaveRoomsAsync([Room("c", 3)], CancellationToken.None);

        var loaded = await db.Settings.LoadAsync(CancellationToken.None);

        Assert.Single(loaded.Rooms);
        Assert.Equal("c", loaded.Rooms[0].Id);
    }

    /// <summary>
    /// Reordering must not trip the unique indexes, which it would if the
    /// replace were not delete-then-insert inside one transaction.
    /// </summary>
    [Fact]
    public async Task Allows_indicators_to_be_swapped_between_rooms()
    {
        using var db = new TestDatabase();

        await db.Settings.SaveRoomsAsync([Room("a", 1), Room("b", 2)], CancellationToken.None);
        await db.Settings.SaveRoomsAsync([Room("a", 2), Room("b", 1)], CancellationToken.None);

        var loaded = await db.Settings.LoadAsync(CancellationToken.None);

        Assert.Equal<byte>(2, loaded.Rooms.Single(r => r.Id == "a").SignalId);
        Assert.Equal<byte>(1, loaded.Rooms.Single(r => r.Id == "b").SignalId);
    }

    [Fact]
    public async Task Keeps_the_order_rooms_were_saved_in()
    {
        using var db = new TestDatabase();

        await db.Settings.SaveRoomsAsync(
            [Room("zebra", 1), Room("aardvark", 2)], CancellationToken.None);

        var loaded = await db.Settings.LoadAsync(CancellationToken.None);

        Assert.Equal(new[] { "zebra", "aardvark" }, loaded.Rooms.Select(r => r.Id).ToArray());
    }

    /// <summary>
    /// The guard that stops an administrator's deliberate deletions coming back
    /// from config/rooms.json on the next restart.
    /// </summary>
    [Fact]
    public async Task Tracks_whether_the_first_run_seed_has_happened()
    {
        using var db = new TestDatabase();

        Assert.False(await db.Settings.IsSeededAsync(CancellationToken.None));

        await db.Settings.MarkSeededAsync(CancellationToken.None);

        Assert.True(await db.Settings.IsSeededAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Counts_failed_logins_within_the_window()
    {
        using var db = new TestDatabase();
        var now = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

        await db.Settings.RecordLoginAttemptAsync(
            now - TimeSpan.FromMinutes(20), false, CancellationToken.None);
        await db.Settings.RecordLoginAttemptAsync(
            now - TimeSpan.FromMinutes(2), false, CancellationToken.None);
        await db.Settings.RecordLoginAttemptAsync(
            now - TimeSpan.FromMinutes(1), true, CancellationToken.None);

        var recent = await db.Settings.CountFailedLoginsAsync(
            now - TimeSpan.FromMinutes(5), CancellationToken.None);

        // Only the failure two minutes ago: the older one is outside the window
        // and the successful one is not a failure.
        Assert.Equal(1, recent);
    }

    [Fact]
    public async Task Clears_the_login_ledger()
    {
        using var db = new TestDatabase();
        var now = DateTimeOffset.UtcNow;

        await db.Settings.RecordLoginAttemptAsync(now, false, CancellationToken.None);
        await db.Settings.ClearLoginAttemptsAsync(CancellationToken.None);

        Assert.Equal(
            0,
            await db.Settings.CountFailedLoginsAsync(
                now - TimeSpan.FromHours(1), CancellationToken.None));
    }

    private static Room Room(string id, byte signalId) => new()
    {
        Id = id,
        Mailbox = $"{id}@example.com",
        DisplayName = id,
        Floor = "Begane grond",
        Directions = "Rechtdoor.",
        SignalId = signalId,
    };
}
