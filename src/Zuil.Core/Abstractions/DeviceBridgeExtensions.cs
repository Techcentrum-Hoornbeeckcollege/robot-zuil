using Zuil.Core.Devices;

namespace Zuil.Core.Abstractions;

/// <summary>
/// The handful of commands the kiosk's own logic sends, named.
///
/// Extension methods rather than interface members: they are built entirely out
/// of the catalogue and <see cref="IDeviceBridge.SendAsync"/>, so an
/// implementation cannot get one of them subtly wrong, and adding another costs
/// an implementer nothing.
/// </summary>
public static class DeviceBridgeExtensions
{
    /// <summary>Round-trips a ping to confirm the device is alive and framing works.</summary>
    public static async Task<bool> PingAsync(this IDeviceBridge bridge, CancellationToken ct)
    {
        var reply = await bridge.SendAsync(DeviceCatalogue.Ping.Build(), ct);
        return reply is { Code: (byte)DeviceResponse.Pong };
    }

    /// <summary>Lights the indicator that points at the given room.</summary>
    public static async Task ShowDirectionAsync(
        this IDeviceBridge bridge,
        byte signalId,
        CancellationToken ct)
    {
        if (!DeviceCatalogue.ShowDirection.TryBuild(
                new Dictionary<string, long>(StringComparer.Ordinal) { ["signalId"] = signalId },
                out var instruction,
                out var errors))
        {
            // signalId is a byte and the parameter accepts the whole byte range,
            // so this is unreachable unless the declaration changed under us.
            throw new InvalidOperationException(
                $"signalId {signalId} is not valid: {errors[0].Message}");
        }

        await bridge.SendAsync(instruction, ct);
    }

    /// <summary>Returns the column to its idle/attract state.</summary>
    public static async Task ClearAsync(this IDeviceBridge bridge, CancellationToken ct) =>
        await bridge.SendAsync(DeviceCatalogue.Clear.Build(), ct);

    /// <summary>Collects whatever the device raised the attention line for.</summary>
    public static async Task<DeviceReply?> ReadEventAsync(
        this IDeviceBridge bridge,
        CancellationToken ct) =>
        await bridge.SendAsync(DeviceCatalogue.ReadEvent.Build(), ct);
}
