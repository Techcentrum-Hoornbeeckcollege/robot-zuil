using Zuil.Core.Models;
using Zuil.Core.Text;

namespace Zuil.Storage.Tests;

/// <summary>
/// The cache the kiosk actually reads from, including the two agenda queries
/// and the exact-match rule the whole privacy posture rests on.
/// </summary>
public class MeetingStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(2));

    private static Meeting Meeting(
        string id,
        string roomId,
        int startsInMinutes,
        int lengthMinutes = 30,
        string? subject = null,
        bool cancelled = false,
        params string[] attendees) => new()
        {
            Id = id,
            RoomId = roomId,
            Subject = subject,
            Start = Now.AddMinutes(startsInMinutes),
            End = Now.AddMinutes(startsInMinutes + lengthMinutes),
            IsCancelled = cancelled,
            Attendees = [.. attendees.Select(name => new Attendee
            {
                Name = name,
                NormalizedName = NameNormalizer.Normalize(name),
            })],
        };

    private static async Task<TestDatabase> SeededAsync(params Meeting[] meetings)
    {
        var db = new TestDatabase();

        await db.Meetings.ReplaceWindowAsync(
            Now.AddHours(-2), Now.AddHours(36), meetings, CancellationToken.None);

        return db;
    }

    // --- Exact-match lookup -------------------------------------------------

    [Fact]
    public async Task Finds_an_attendee_by_their_full_name()
    {
        using var db = await SeededAsync(
            Meeting("m1", "a", 30, attendees: "Jonathan Nap"));

        var found = await db.Meetings.FindByAttendeeAsync(
            NameNormalizer.Normalize("Jonathan Nap"), Now, TimeSpan.FromHours(36),
            CancellationToken.None);

        Assert.Single(found);
        Assert.Equal("m1", found[0].Id);
    }

    /// <summary>
    /// The decision docs/privacy.md leans on hardest: a prefix search would turn
    /// the column into a browsable directory of everyone in the building — type
    /// "a" and read the lobby.
    /// </summary>
    [Theory]
    [InlineData("jonathan")]
    [InlineData("nap")]
    [InlineData("j")]
    [InlineData("jonathan na")]
    public async Task Never_matches_a_partial_name(string partial)
    {
        using var db = await SeededAsync(
            Meeting("m1", "a", 30, attendees: "Jonathan Nap"));

        var found = await db.Meetings.FindByAttendeeAsync(
            partial, Now, TimeSpan.FromHours(36), CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task Does_not_hydrate_attendees_on_read()
    {
        using var db = await SeededAsync(
            Meeting("m1", "a", 30, attendees: "Jonathan Nap", subject: "Intake"));

        var found = await db.Meetings.FindByAttendeeAsync(
            NameNormalizer.Normalize("Jonathan Nap"), Now, TimeSpan.FromHours(36),
            CancellationToken.None);

        // Not loading them is what stops them leaking into a response by
        // accident.
        Assert.Empty(found[0].Attendees);
    }

    // --- The atomic window swap --------------------------------------------

    /// <summary>
    /// A reader must see either the whole old window or the whole new one. The
    /// observable half of that invariant: nothing from the previous window
    /// survives the swap.
    /// </summary>
    [Fact]
    public async Task Replaces_the_window_atomically()
    {
        using var db = await SeededAsync(
            Meeting("old", "a", 30, attendees: "Jonathan Nap"));

        await db.Meetings.ReplaceWindowAsync(
            Now.AddHours(-2),
            Now.AddHours(36),
            [Meeting("new", "a", 60, attendees: "Sanne de Vries")],
            CancellationToken.None);

        var gone = await db.Meetings.FindByAttendeeAsync(
            NameNormalizer.Normalize("Jonathan Nap"), Now, TimeSpan.FromHours(36),
            CancellationToken.None);
        var present = await db.Meetings.FindByAttendeeAsync(
            NameNormalizer.Normalize("Sanne de Vries"), Now, TimeSpan.FromHours(36),
            CancellationToken.None);

        Assert.Empty(gone);
        Assert.Single(present);

        var all = await db.Meetings.GetUpcomingAsync(
            Now, TimeSpan.FromHours(36), 100, CancellationToken.None);
        Assert.Single(all);
    }

    [Fact]
    public async Task Records_a_successful_sync()
    {
        using var db = await SeededAsync(Meeting("m1", "a", 30));

        var status = await db.Meetings.GetSyncStatusAsync(CancellationToken.None);

        Assert.NotNull(status.LastSuccessfulSync);
        Assert.Null(status.LastError);
        Assert.Equal(1, status.MeetingCount);
    }

    [Fact]
    public async Task Records_a_failed_sync_without_losing_the_cache()
    {
        using var db = await SeededAsync(Meeting("m1", "a", 30));

        await db.Meetings.RecordSyncAttemptAsync(
            Now, "Geen verbinding met Outlook.", CancellationToken.None);

        var status = await db.Meetings.GetSyncStatusAsync(CancellationToken.None);

        Assert.Equal("Geen verbinding met Outlook.", status.LastError);
        Assert.NotNull(status.LastSuccessfulSync);
        Assert.Equal(1, status.MeetingCount);
    }

    // --- Agenda queries ----------------------------------------------------

    [Fact]
    public async Task Upcoming_is_ordered_by_start_across_rooms()
    {
        using var db = await SeededAsync(
            Meeting("late", "c", 120),
            Meeting("early", "a", 15),
            Meeting("middle", "b", 60));

        var upcoming = await db.Meetings.GetUpcomingAsync(
            Now, TimeSpan.FromHours(36), 100, CancellationToken.None);

        Assert.Equal(["early", "middle", "late"], upcoming.Select(m => m.Id));
    }

    /// <summary>
    /// Ties break on room, so the list does not reshuffle under a visitor's eyes
    /// every time the cache refreshes.
    /// </summary>
    [Fact]
    public async Task Upcoming_breaks_ties_on_room()
    {
        using var db = await SeededAsync(
            Meeting("z", "zaal-z", 30),
            Meeting("a", "zaal-a", 30));

        var first = await db.Meetings.GetUpcomingAsync(
            Now, TimeSpan.FromHours(36), 100, CancellationToken.None);
        var second = await db.Meetings.GetUpcomingAsync(
            Now, TimeSpan.FromHours(36), 100, CancellationToken.None);

        Assert.Equal(["zaal-a", "zaal-z"], first.Select(m => m.RoomId));
        Assert.Equal(first.Select(m => m.Id), second.Select(m => m.Id));
    }

    [Fact]
    public async Task Upcoming_respects_the_limit()
    {
        using var db = await SeededAsync(
            Meeting("m1", "a", 10),
            Meeting("m2", "a", 20),
            Meeting("m3", "a", 30));

        var upcoming = await db.Meetings.GetUpcomingAsync(
            Now, TimeSpan.FromHours(36), 2, CancellationToken.None);

        Assert.Equal(2, upcoming.Count);
    }

    [Fact]
    public async Task Upcoming_excludes_cancelled_and_finished_meetings()
    {
        using var db = await SeededAsync(
            Meeting("cancelled", "a", 30, cancelled: true),
            Meeting("finished", "a", -120, lengthMinutes: 30),
            Meeting("live", "a", -10, lengthMinutes: 60),
            Meeting("later", "a", 30));

        var upcoming = await db.Meetings.GetUpcomingAsync(
            Now, TimeSpan.FromHours(36), 100, CancellationToken.None);

        // A meeting already in progress still counts: that is the one someone
        // walking in is looking for.
        Assert.Equal(["live", "later"], upcoming.Select(m => m.Id));
    }

    [Fact]
    public async Task Upcoming_stops_at_the_lookahead()
    {
        using var db = await SeededAsync(
            Meeting("soon", "a", 30),
            Meeting("tomorrow", "a", 60 * 20));

        var upcoming = await db.Meetings.GetUpcomingAsync(
            Now, TimeSpan.FromHours(4), 100, CancellationToken.None);

        Assert.Equal(["soon"], upcoming.Select(m => m.Id));
    }

    [Fact]
    public async Task Upcoming_carries_the_subject_for_the_projection_to_decide_on()
    {
        using var db = await SeededAsync(Meeting("m1", "a", 30, subject: "Sprint review"));

        var upcoming = await db.Meetings.GetUpcomingAsync(
            Now, TimeSpan.FromHours(36), 100, CancellationToken.None);

        // The store does not gate; AgendaProjection does. If this ever returned
        // null the AdminOnly visibility would silently stop working.
        Assert.Equal("Sprint review", upcoming[0].Subject);
    }

    /// <summary>
    /// Overlap, not containment: a meeting that started before the window still
    /// occupies the room someone is standing in front of.
    /// </summary>
    [Fact]
    public async Task Window_includes_a_meeting_that_straddles_the_start()
    {
        using var db = await SeededAsync(
            Meeting("straddles", "a", -30, lengthMinutes: 90),
            Meeting("before", "a", -120, lengthMinutes: 30));

        var window = await db.Meetings.GetWindowAsync(
            Now, Now.AddHours(8), CancellationToken.None);

        Assert.Equal(["straddles"], window.Select(m => m.Id));
    }

    [Fact]
    public async Task Window_is_grouped_by_room_then_time()
    {
        using var db = await SeededAsync(
            Meeting("b2", "zaal-b", 90),
            Meeting("a2", "zaal-a", 90),
            Meeting("a1", "zaal-a", 30),
            Meeting("b1", "zaal-b", 30));

        var window = await db.Meetings.GetWindowAsync(
            Now, Now.AddHours(8), CancellationToken.None);

        Assert.Equal(["a1", "a2", "b1", "b2"], window.Select(m => m.Id));
    }

    [Fact]
    public async Task Window_excludes_a_meeting_ending_exactly_at_the_start()
    {
        using var db = await SeededAsync(
            Meeting("ends-on-the-boundary", "a", -30, lengthMinutes: 30));

        var window = await db.Meetings.GetWindowAsync(
            Now, Now.AddHours(8), CancellationToken.None);

        Assert.Empty(window);
    }

    // --- Retention ---------------------------------------------------------

    [Fact]
    public async Task Purge_drops_finished_meetings_and_their_attendees()
    {
        using var db = await SeededAsync(
            Meeting("finished", "a", -180, lengthMinutes: 30, attendees: "Jonathan Nap"),
            Meeting("live", "a", -10, lengthMinutes: 60));

        await db.Meetings.PurgeBeforeAsync(Now.AddHours(-1), CancellationToken.None);

        var status = await db.Meetings.GetSyncStatusAsync(CancellationToken.None);
        Assert.Equal(1, status.MeetingCount);

        // The cascade only fires because the connection sets foreign_keys=ON.
        var orphaned = await db.Meetings.FindByAttendeeAsync(
            NameNormalizer.Normalize("Jonathan Nap"),
            Now.AddHours(-4),
            TimeSpan.FromHours(36),
            CancellationToken.None);
        Assert.Empty(orphaned);
    }

    [Fact]
    public async Task By_room_returns_only_that_room()
    {
        using var db = await SeededAsync(
            Meeting("a1", "zaal-a", 30),
            Meeting("b1", "zaal-b", 30));

        var found = await db.Meetings.GetByRoomAsync(
            "zaal-a", Now, TimeSpan.FromHours(36), CancellationToken.None);

        Assert.Equal(["a1"], found.Select(m => m.Id));
    }
}
