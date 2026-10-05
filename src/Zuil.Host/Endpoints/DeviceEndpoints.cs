using Zuil.Core.Abstractions;

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
            await device.ClearAsync(ct);
            return Results.NoContent();
        });

        // No endpoint takes a raw command byte or payload. Everything the HTTP
        // surface can do maps to a named, allowlisted operation, because this API
        // is reachable from a touchscreen in a public lobby.
    }
}
