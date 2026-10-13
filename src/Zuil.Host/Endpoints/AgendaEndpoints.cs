using Zuil.Core.Abstractions;
using Zuil.Core.Admin;
using Zuil.Core.Agenda;
using Zuil.Core.Models;
using Zuil.Core.Settings;
using Zuil.Host.Admin;

namespace Zuil.Host.Endpoints;

/// <summary>
/// The two agenda views, plus the small public config the SPA needs at startup.
///
/// Worth being clear about what these are: docs/privacy.md says there is no
/// endpoint listing meetings across the building, and these are that endpoint.
/// With subjects hidden — the shipped default — they publish a time and a room
/// and nothing else, which is not personal data, and that is the whole reason
/// the default has to stay hidden and the gate has to be on this side of the
/// wire.
///
/// Neither route touches the device. The existing GET /api/rooms/{id}/schedule
/// lights an indicator as a side effect of a read; repeating that here would
/// mean the column swings about whenever somebody walks past and glances at the
/// board.
/// </summary>
public static class AgendaEndpoints
{
    public static void MapAgendaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/agenda/upcoming", async (
            int? limit,
            HttpContext context,
            IMeetingStore store,
            IRoomDirectory rooms,
            IClock clock,
            ISettingsProvider settings,
            AdminSessionStore sessions,
            CancellationToken ct) =>
        {
            var current = settings.Current;
            var now = clock.Now;
            var isAdmin = IsAdmin(context, sessions);

            var count = Math.Clamp(limit ?? current.Ui.UpcomingCount, 1, 100);

            var meetings = await store.GetUpcomingAsync(
                now, current.Sync.WindowAhead, count, ct);

            var entries = Project(meetings, rooms, current.Privacy, isAdmin);

            var status = await store.GetSyncStatusAsync(ct);

            return Results.Ok(new UpcomingAgendaResponse(
                Now: now,
                Stale: status.IsStale(now, current.Sync.StaleAfter),
                ShowSubjects: AgendaProjection.MaySeeSubject(
                    current.Privacy.SubjectVisibility, isAdmin),
                Entries: entries));
        });

        app.MapGet("/api/agenda/rooms", async (
            DateOnly? date,
            HttpContext context,
            IMeetingStore store,
            IRoomDirectory rooms,
            IClock clock,
            ISettingsProvider settings,
            AdminSessionStore sessions,
            CancellationToken ct) =>
        {
            var current = settings.Current;
            var now = clock.Now;
            var isAdmin = IsAdmin(context, sessions);

            var tz = ResolveTimeZone(current.Graph.TimeZone);
            var (from, to) = DayWindow(date, now, tz);

            var meetings = await store.GetWindowAsync(from, to, ct);
            var byRoom = meetings
                .GroupBy(m => m.RoomId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // Driven by the directory, not by what happens to be cached, so an
            // empty room still gets a column rather than silently vanishing.
            var columns = rooms.All
                .Select(room => new RoomAgenda(
                    Id: room.Id,
                    DisplayName: room.DisplayName,
                    Floor: room.Floor,
                    Entries: Project(
                        byRoom.GetValueOrDefault(room.Id) ?? [],
                        rooms,
                        current.Privacy,
                        isAdmin)))
                .ToList();

            var status = await store.GetSyncStatusAsync(ct);

            // The cache only holds the synced window. Say so rather than showing
            // an empty Thursday as though Thursday were free.
            var cachedFrom = now - current.Sync.WindowBehind;
            var cachedTo = now + current.Sync.WindowAhead;
            var truncated = from < cachedFrom || to > cachedTo;

            return Results.Ok(new RoomAgendasResponse(
                Now: now,
                From: from,
                To: to,
                Truncated: truncated,
                Stale: status.IsStale(now, current.Sync.StaleAfter),
                ShowSubjects: AgendaProjection.MaySeeSubject(
                    current.Privacy.SubjectVisibility, isAdmin),
                Rooms: columns));
        });

        // Public, and deliberately free of anything that is not already on
        // screen: the SPA needs the idle timeout and the resting view before it
        // can render its first frame.
        app.MapGet("/api/kiosk-config", (
            HttpContext context,
            ISettingsProvider settings,
            AdminSessionStore sessions) =>
        {
            var current = settings.Current;
            var isAdmin = IsAdmin(context, sessions);

            return Results.Ok(new KioskConfigResponse(
                IdleTimeoutMs: (int)current.Privacy.IdleTimeout.TotalMilliseconds,
                AttractView: current.Ui.AttractView.ToString(),
                UpcomingCount: current.Ui.UpcomingCount,
                ShowSubjects: AgendaProjection.MaySeeSubject(
                    current.Privacy.SubjectVisibility, isAdmin),
                ShowAgendaNav: current.Ui.ShowAgendaNav,
                HasRooms: current.Rooms.Count > 0));
        });
    }

    /// <summary>
    /// Whether this request carries a live admin session, which is what the
    /// AdminOnly subject visibility turns on. Checked here rather than behind the
    /// admin filter because these routes must answer a visitor too.
    /// </summary>
    private static bool IsAdmin(HttpContext context, AdminSessionStore sessions) =>
        AdminSession.TokenFrom(context.Request) is { } token
        && sessions.Validate(AdminSession.Hash(token));

    private static List<AgendaEntry> Project(
        IReadOnlyList<Meeting> meetings,
        IRoomDirectory rooms,
        PrivacySettings privacy,
        bool isAdmin)
    {
        var entries = new List<AgendaEntry>(meetings.Count);

        foreach (var meeting in meetings)
        {
            // A meeting whose room has since been deleted has nowhere to send
            // anyone, so it is not worth a row.
            if (rooms.ById(meeting.RoomId) is not { } room)
            {
                continue;
            }

            entries.Add(AgendaProjection.Project(meeting, room, privacy, isAdmin));
        }

        return entries;
    }

    /// <summary>
    /// The local day containing <paramref name="date"/>, or today, in the
    /// configured timezone — so "today" means today in the building, not in UTC.
    /// </summary>
    private static (DateTimeOffset From, DateTimeOffset To) DayWindow(
        DateOnly? date, DateTimeOffset now, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(now, tz);
        var day = date ?? DateOnly.FromDateTime(local.Date);

        var startLocal = day.ToDateTime(TimeOnly.MinValue);
        var offset = tz.GetUtcOffset(startLocal);
        var from = new DateTimeOffset(startLocal, offset);

        // Add a day rather than 24 hours, so a DST boundary does not clip an
        // hour off the end of the board.
        var endLocal = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var to = new DateTimeOffset(endLocal, tz.GetUtcOffset(endLocal));

        return (from, to);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A bad timezone must not take the board down; the admin panel
            // validates it, and the diagnostics page reports it.
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>"Straks": what is on across the whole building, soonest first.</summary>
public sealed record UpcomingAgendaResponse(
    DateTimeOffset Now,
    bool Stale,
    bool ShowSubjects,
    IReadOnlyList<AgendaEntry> Entries);

/// <summary>One room's column on the board.</summary>
public sealed record RoomAgenda(
    string Id,
    string DisplayName,
    string Floor,
    IReadOnlyList<AgendaEntry> Entries);

public sealed record RoomAgendasResponse(
    DateTimeOffset Now,
    DateTimeOffset From,
    DateTimeOffset To,
    bool Truncated,
    bool Stale,
    bool ShowSubjects,
    IReadOnlyList<RoomAgenda> Rooms);

/// <summary>
/// What the SPA needs before its first render. <c>ShowSubjects</c> is advisory —
/// it lets the board lay out a subject column — and is never what keeps a
/// subject off the screen. That is done by the subject being null.
/// </summary>
public sealed record KioskConfigResponse(
    int IdleTimeoutMs,
    string AttractView,
    int UpcomingCount,
    bool ShowSubjects,
    bool ShowAgendaNav,
    bool HasRooms);
