using Zuil.Core.Abstractions;
using Zuil.Core.Models;

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
            ISettingsProvider settings,
            CancellationToken ct) =>
        {
            var status = await store.GetSyncStatusAsync(ct);
            var staleAfter = settings.Current.Sync.StaleAfter;

            return Results.Ok(new
            {
                now = clock.Now,
                clockSynchronized = clock.IsSynchronized,
                deviceConnected = device.IsConnected,
                lastSuccessfulSync = status.LastSuccessfulSync,
                lastAttemptedSync = status.LastAttemptedSync,

                // Whether there is an error, never what it said. This endpoint
                // takes unauthenticated callers and a Graph failure can quote a
                // response body; the admin diagnostics endpoint carries the text.
                hasError = status.LastError is not null,

                meetingCount = status.MeetingCount,
                stale = status.IsStale(clock.Now, staleAfter),

                // Rolled up by Core, so the dot on the kiosk and the badge in the
                // admin panel cannot drift apart from each other.
                status = HealthEvaluation
                    .Evaluate(
                        status,
                        clock.IsSynchronized,
                        device.IsConnected,
                        clock.Now,
                        staleAfter)
                    .ToString(),
            });
        });
    }
}
