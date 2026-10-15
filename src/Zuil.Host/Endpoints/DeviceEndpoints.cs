using Zuil.Core.Abstractions;
using Zuil.Core.Devices;

namespace Zuil.Host.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/device");

        group.MapGet("/status", (IDeviceBridge device) =>
            Results.Ok(new { connected = device.IsConnected }));

        group.MapPost("/clear", async (IDeviceBridge device, CancellationToken ct) =>
        {
            // Checked rather than assumed: the catalogue decides what a visitor
            // may reach, so marking a command admin-only there closes this route
            // too, instead of leaving a second list to remember.
            if (!DeviceCatalogue.Clear.AllowedFromKiosk)
            {
                return Results.NotFound();
            }

            await device.SendAsync(DeviceCatalogue.Clear.Build(), ct);
            return Results.NoContent();
        });

        // No endpoint here takes a command key, a command byte or a payload.
        // Everything this API can do is one fixed operation, because it is
        // reachable from a touchscreen in a public lobby — the catalogue-driven
        // surface, where any declared command can be sent, is behind the admin
        // session (see AdminEndpoints).
    }
}
