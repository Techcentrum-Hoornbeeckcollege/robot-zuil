using Microsoft.Extensions.Options;
using Zuil.Core.Abstractions;
using Zuil.Host.Workers;

namespace Zuil.Host.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // What the kiosk UI polls to decide whether to show a staleness banner,
        // and what you curl over SSH when the screen in the lobby looks wrong.
        app.MapGet("/api/health", async (
            IMeetingStore store,
            IDeviceBridge device,
            IClock clock,
            IOptions<SyncOptions> sync,
            CancellationToken ct) =>
        {
            var status = await store.GetSyncStatusAsync(ct);

            return Results.Ok(new
            {
                now = clock.Now,
                clockSynchronized = clock.IsSynchronized,
                deviceConnected = device.IsConnected,
                lastSuccessfulSync = status.LastSuccessfulSync,
                lastAttemptedSync = status.LastAttemptedSync,
                lastError = status.LastError,
                meetingCount = status.MeetingCount,
                stale = status.IsStale(clock.Now, sync.Value.StaleAfter),
            });
        });
    }
}
