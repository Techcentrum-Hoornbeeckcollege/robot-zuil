using Microsoft.Extensions.Options;
using Zuil.Core.Abstractions;
using Zuil.Core.Models;
using Zuil.Core.Text;
using Zuil.Host.Workers;

namespace Zuil.Host.Endpoints;

/// <summary>
/// The visitor-facing lookup: a name in, directions out.
/// </summary>
public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        // POST, not GET: a name in a query string ends up in access logs,
        // browser history and the SignalR referrer. See docs/privacy.md.
        app.MapPost("/api/search/by-name", async (
            NameSearchRequest request,
            IMeetingStore store,
            IRoomDirectory rooms,
            IClock clock,
            IDeviceBridge device,
            IOptions<SyncOptions> sync,
            CancellationToken ct) =>
        {
            if (!clock.IsSynchronized)
            {
                return Results.Problem(
                    "The kiosk clock is not yet synchronised.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var normalized = NameNormalizer.Normalize(request.Name ?? "");

            // Require something substantial. A one-character "search" against an
            // exact-match index is harmless, but refusing it keeps the UI honest
            // about this not being a browsable directory.
            if (normalized.Length < 3)
            {
                return Results.BadRequest(new { error = "Enter your full name." });
            }

            var meetings = await store.FindByAttendeeAsync(
                normalized, clock.Now, sync.Value.WindowAhead, ct);

            if (meetings.Count == 0)
            {
                // Deliberately the same response as "no such person": the kiosk
                // must not confirm whether a given name exists in the tenant.
                return Results.Ok(new SearchResponse([], Stale: false));
            }

            var hints = new List<DirectionHint>();

            foreach (var meeting in meetings)
            {
                if (rooms.ById(meeting.RoomId) is not { } room)
                {
                    continue;
                }

                hints.Add(new DirectionHint
                {
                    RoomDisplayName = room.DisplayName,
                    Floor = room.Floor,
                    Directions = room.Directions,
                    Start = meeting.Start,
                    End = meeting.End,
                    SignalId = room.SignalId,
                });
            }

            // Point the physical column at the first (soonest) match.
            if (hints.Count > 0)
            {
                await device.ShowDirectionAsync(hints[0].SignalId, ct);
            }

            var status = await store.GetSyncStatusAsync(ct);
            return Results.Ok(new SearchResponse(
                hints,
                status.IsStale(clock.Now, sync.Value.StaleAfter)));
        });
    }
}

public sealed record NameSearchRequest(string? Name);

public sealed record SearchResponse(IReadOnlyList<DirectionHint> Results, bool Stale);
