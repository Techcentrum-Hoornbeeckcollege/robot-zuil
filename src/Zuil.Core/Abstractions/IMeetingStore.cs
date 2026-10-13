using Zuil.Core.Models;

namespace Zuil.Core.Abstractions;

/// <summary>
/// The local cache the kiosk actually reads from. Every visitor-facing query hits
/// this, never Graph, so the kiosk keeps working through network outages.
/// </summary>
public interface IMeetingStore
{
    /// <summary>
    /// Atomically replaces the cached window with a freshly synced set, so the
    /// kiosk never observes a half-written cache.
    /// </summary>
    Task ReplaceWindowAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyList<Meeting> meetings,
        CancellationToken ct);

    /// <summary>
    /// Exact-match lookup of meetings for an attendee that are current or
    /// upcoming at <paramref name="now"/>.
    ///
    /// Deliberately exact-match and deliberately not paged: a public screen must
    /// not let a passer-by browse who is in the building. See docs/privacy.md.
    /// </summary>
    Task<IReadOnlyList<Meeting>> FindByAttendeeAsync(
        string normalizedName,
        DateTimeOffset now,
        TimeSpan lookahead,
        CancellationToken ct);

    /// <summary>Meetings in one room that are current or upcoming.</summary>
    Task<IReadOnlyList<Meeting>> GetByRoomAsync(
        string roomId,
        DateTimeOffset now,
        TimeSpan lookahead,
        CancellationToken ct);

    /// <summary>
    /// Current and upcoming occurrences across every room, soonest first, capped
    /// at <paramref name="limit"/>. Feeds the "Straks" list.
    ///
    /// Note there is no privacy parameter here. The store always returns the
    /// whole <see cref="Meeting"/>, and exactly one pure function
    /// (Zuil.Core.Agenda.AgendaProjection) decides what may be published.
    /// Gating in the data layer would spread that decision across every call
    /// site, each free to get it wrong.
    /// </summary>
    Task<IReadOnlyList<Meeting>> GetUpcomingAsync(
        DateTimeOffset now,
        TimeSpan lookahead,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// Occurrences overlapping [<paramref name="from"/>, <paramref name="to"/>)
    /// in every room, grouped-friendly order, for the per-room board.
    /// </summary>
    Task<IReadOnlyList<Meeting>> GetWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);

    Task<SyncStatus> GetSyncStatusAsync(CancellationToken ct);

    Task RecordSyncAttemptAsync(DateTimeOffset at, string? error, CancellationToken ct);

    /// <summary>Drops occurrences that ended before <paramref name="before"/>.</summary>
    Task PurgeBeforeAsync(DateTimeOffset before, CancellationToken ct);
}
