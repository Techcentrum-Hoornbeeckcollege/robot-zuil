using Zuil.Core.Agenda;
using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Core.Tests;

/// <summary>
/// The privacy regression test.
///
/// An all-rooms agenda board on a screen in a public lobby is defensible only
/// because, by default, it publishes a time and a room and nothing else. That
/// rests entirely on this one function nulling the subject — not on the client
/// choosing not to render it, because a subject that reaches the browser has
/// already left the building. So every combination is pinned here.
/// </summary>
public class AgendaProjectionTests
{
    private static readonly Room Room = new()
    {
        Id = "zaal-1-01",
        Mailbox = "zaal-1-01@example.com",
        DisplayName = "Vergaderzaal 1.01",
        Floor = "1e verdieping",
        Directions = "Trap op, dan rechts.",
        SignalId = 3,
    };

    private static readonly Meeting Meeting = new()
    {
        Id = "abc",
        RoomId = "zaal-1-01",
        Subject = "Exitgesprek J. Nap",
        Start = new DateTimeOffset(2026, 10, 8, 9, 30, 0, TimeSpan.FromHours(2)),
        End = new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.FromHours(2)),
        Attendees = [],
    };

    [Theory]
    [InlineData(SubjectVisibility.Hidden, false)]
    [InlineData(SubjectVisibility.Hidden, true)]
    [InlineData(SubjectVisibility.AdminOnly, false)]
    public void Withholds_the_subject(SubjectVisibility visibility, bool isAdmin)
    {
        var entry = Project(visibility, isAdmin);

        Assert.Null(entry.Subject);
    }

    [Theory]
    [InlineData(SubjectVisibility.AdminOnly, true)]
    [InlineData(SubjectVisibility.Everyone, false)]
    [InlineData(SubjectVisibility.Everyone, true)]
    public void Publishes_the_subject(SubjectVisibility visibility, bool isAdmin)
    {
        var entry = Project(visibility, isAdmin);

        Assert.Equal("Exitgesprek J. Nap", entry.Subject);
    }

    /// <summary>
    /// The shipped default. If this ever flips, a fresh install starts putting
    /// meeting titles on a lobby screen without anyone deciding to.
    /// </summary>
    [Fact]
    public void Defaults_to_hiding_subjects()
    {
        var entry = AgendaProjection.Project(Meeting, Room, new PrivacySettings(), isAdmin: false);

        Assert.Null(entry.Subject);
    }

    [Fact]
    public void Always_publishes_the_room_and_the_time()
    {
        var entry = Project(SubjectVisibility.Hidden, isAdmin: false);

        Assert.Equal("zaal-1-01", entry.RoomId);
        Assert.Equal("Vergaderzaal 1.01", entry.RoomDisplayName);
        Assert.Equal("1e verdieping", entry.Floor);
        Assert.Equal(Meeting.Start, entry.Start);
        Assert.Equal(Meeting.End, entry.End);
    }

    /// <summary>
    /// A null subject in the cache must not become an empty string on the way
    /// out: the client distinguishes "no subject" from "subject withheld" only
    /// by it being null.
    /// </summary>
    [Fact]
    public void Keeps_a_missing_subject_null()
    {
        var entry = AgendaProjection.Project(
            Meeting with { Subject = null },
            Room,
            new PrivacySettings { SubjectVisibility = SubjectVisibility.Everyone },
            isAdmin: true);

        Assert.Null(entry.Subject);
    }

    [Theory]
    [InlineData(SubjectVisibility.Hidden, false, false)]
    [InlineData(SubjectVisibility.Hidden, true, false)]
    [InlineData(SubjectVisibility.AdminOnly, false, false)]
    [InlineData(SubjectVisibility.AdminOnly, true, true)]
    [InlineData(SubjectVisibility.Everyone, false, true)]
    [InlineData(SubjectVisibility.Everyone, true, true)]
    public void MaySeeSubject_matches_the_projection(
        SubjectVisibility visibility, bool isAdmin, bool expected) =>
        Assert.Equal(expected, AgendaProjection.MaySeeSubject(visibility, isAdmin));

    private static AgendaEntry Project(SubjectVisibility visibility, bool isAdmin) =>
        AgendaProjection.Project(
            Meeting,
            Room,
            new PrivacySettings { SubjectVisibility = visibility },
            isAdmin);
}
