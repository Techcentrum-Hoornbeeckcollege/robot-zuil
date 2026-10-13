using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Core.Agenda;

/// <summary>
/// One row of an agenda, as the kiosk is allowed to show it.
/// </summary>
/// <param name="RoomId">Stable room id, for grouping.</param>
/// <param name="RoomDisplayName">Name a visitor reads.</param>
/// <param name="Floor">Which floor, so the list is useful without tapping through.</param>
/// <param name="Subject">
/// Null unless the configured visibility allows this caller to see it. Null in
/// the JSON, not merely unrendered — see <see cref="AgendaProjection"/>.
/// </param>
public sealed record AgendaEntry(
    string RoomId,
    string RoomDisplayName,
    string Floor,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Subject);

/// <summary>
/// Turns a cached <see cref="Meeting"/> into something publishable.
///
/// This is the only place the subject privacy rule is applied, and it is applied
/// by nulling the field rather than by asking the client not to render it: a
/// subject that reaches the browser has already left the building, whatever the
/// CSS says. One function means no endpoint can forget, and one test covers
/// every combination.
///
/// The store deliberately does not take a privacy flag. Gating in the data layer
/// spreads the rule across call sites, each of which can get it wrong.
/// </summary>
public static class AgendaProjection
{
    public static AgendaEntry Project(
        Meeting meeting,
        Room room,
        PrivacySettings privacy,
        bool isAdmin) =>
        new(
            RoomId: room.Id,
            RoomDisplayName: room.DisplayName,
            Floor: room.Floor,
            Start: meeting.Start,
            End: meeting.End,
            Subject: MaySeeSubject(privacy.SubjectVisibility, isAdmin) ? meeting.Subject : null);

    /// <summary>
    /// Whether this caller may see meeting subjects.
    ///
    /// <see cref="SubjectVisibility.AdminOnly"/> exists so staff can diagnose a
    /// "sent to the wrong room" report without turning the lobby screen into a
    /// public board of meeting titles.
    /// </summary>
    public static bool MaySeeSubject(SubjectVisibility visibility, bool isAdmin) =>
        visibility switch
        {
            SubjectVisibility.Everyone => true,
            SubjectVisibility.AdminOnly => isAdmin,
            _ => false,
        };
}
