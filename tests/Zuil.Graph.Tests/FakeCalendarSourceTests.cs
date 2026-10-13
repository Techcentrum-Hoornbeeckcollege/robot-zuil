using Zuil.Core.Abstractions;
using Zuil.Core.Models;
using Zuil.Core.Text;

namespace Zuil.Graph.Tests;

internal sealed class FixedClock : IClock
{
    public DateTimeOffset Now { get; init; } = new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(2));

    public bool IsSynchronized => true;
}

/// <summary>
/// The fake is what makes the kiosk developable on a laptop with no tenant, no
/// Pi and no ESP32, and ./build/dev.sh is the main way anyone looks at this
/// project. If it stops producing usable data the whole dev loop goes with it.
///
/// TODO: cover the real CalendarViewClient against recorded Graph responses —
/// the /calendarView mapping, DST handling in ToOffset, @odata.nextLink paging,
/// and that attendee email addresses are dropped. It needs GraphTokenProvider
/// behind an interface first, since the client takes the concrete class.
/// </summary>
public class FakeCalendarSourceTests
{
    private static readonly Room Room = new()
    {
        Id = "zaal-1-01",
        Mailbox = "zaal-1-01@example.com",
        DisplayName = "Vergaderzaal 1.01",
        Floor = "1e verdieping",
        Directions = "Trap op.",
        SignalId = 1,
    };

    private static Task<IReadOnlyList<Meeting>> OccurrencesAsync(Room? room = null)
    {
        var clock = new FixedClock();
        var source = new FakeCalendarSource(clock);

        return source.GetOccurrencesAsync(
            room ?? Room,
            clock.Now.AddHours(-2),
            clock.Now.AddHours(36),
            CancellationToken.None);
    }

    [Fact]
    public async Task Produces_meetings_for_a_room()
    {
        var meetings = await OccurrencesAsync();

        Assert.NotEmpty(meetings);
        Assert.All(meetings, m => Assert.Equal(Room.Id, m.RoomId));
    }

    /// <summary>
    /// Deterministic per room, so restarting the dev backend does not reshuffle
    /// the data you were just looking at — and so a name you searched for a
    /// minute ago is still there.
    /// </summary>
    [Fact]
    public async Task Is_deterministic_for_the_same_room()
    {
        var first = await OccurrencesAsync();
        var second = await OccurrencesAsync();

        Assert.Equal(
            first.Select(m => (m.Id, m.Start, m.Attendees[0].Name)),
            second.Select(m => (m.Id, m.Start, m.Attendees[0].Name)));
    }

    [Fact]
    public async Task Gives_different_rooms_different_meetings()
    {
        var first = await OccurrencesAsync();
        var second = await OccurrencesAsync(Room with { Id = "zaal-2-02", SignalId = 6 });

        Assert.NotEqual(first.Select(m => m.Id), second.Select(m => m.Id));
    }

    /// <summary>
    /// The attendee lookup is an equality probe on the normalized name, so a
    /// fake whose names are not normalized the same way would make every search
    /// in the dev loop silently return nothing.
    /// </summary>
    [Fact]
    public async Task Normalizes_attendee_names_the_way_the_search_does()
    {
        var meetings = await OccurrencesAsync();

        Assert.All(meetings, meeting =>
            Assert.All(meeting.Attendees, attendee =>
                Assert.Equal(
                    NameNormalizer.Normalize(attendee.Name),
                    attendee.NormalizedName)));
    }

    [Fact]
    public async Task Stays_inside_the_requested_window()
    {
        var clock = new FixedClock();
        var to = clock.Now.AddHours(36);

        var meetings = await OccurrencesAsync();

        Assert.All(meetings, m => Assert.True(m.Start < to));
    }

    [Fact]
    public async Task Produces_meetings_that_have_not_finished()
    {
        var clock = new FixedClock();

        var meetings = await OccurrencesAsync();

        // At least one is current or still to come, or the dev loop opens on an
        // empty agenda every time.
        Assert.Contains(meetings, m => m.End > clock.Now);
    }
}
