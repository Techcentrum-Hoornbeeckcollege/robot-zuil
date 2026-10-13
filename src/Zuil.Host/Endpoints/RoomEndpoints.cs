using Zuil.Core.Abstractions;

namespace Zuil.Host.Endpoints;

public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this IEndpointRouteBuilder app)
    {
        // Room list for the "browse by room" path on the kiosk. Rooms are not
        // personal data, so unlike name search this is freely listable.
        app.MapGet("/api/rooms", (IRoomDirectory rooms) =>
            Results.Ok(rooms.All.Select(r => new
            {
                r.Id,
                r.DisplayName,
                r.Floor,
                r.Directions,
            })));

        app.MapGet("/api/rooms/{id}/schedule", async (
            string id,
            IMeetingStore store,
            IRoomDirectory rooms,
            IClock clock,
            IDeviceBridge device,
            ISettingsProvider settings,
            CancellationToken ct) =>
        {
            if (rooms.ById(id) is not { } room)
            {
                return Results.NotFound();
            }

            var meetings = await store.GetByRoomAsync(
                room.Id, clock.Now, settings.Current.Sync.WindowAhead, ct);

            await device.ShowDirectionAsync(room.SignalId, ct);

            // Subject is omitted on purpose: meeting titles leak more than names
            // do, and a passer-by does not need them to find a room.
            return Results.Ok(new
            {
                room = new { room.Id, room.DisplayName, room.Floor, room.Directions },
                meetings = meetings.Select(m => new { m.Start, m.End }),
            });
        });
    }
}
