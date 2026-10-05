namespace Zuil.Core.Abstractions;

/// <summary>
/// Talks to the ESP32 that drives the physical wayfinding indicators.
///
/// The transport lives entirely behind this interface. The default
/// implementation is i2c with the Pi as master, but ESP32 i2c-slave support is
/// the fragile part of that design — if it misbehaves on the bench, a UART
/// implementation drops in here without touching anything else.
/// </summary>
public interface IDeviceBridge
{
    /// <summary>True once the device has answered a ping.</summary>
    bool IsConnected { get; }

    /// <summary>Round-trips a ping to confirm the device is alive and framing works.</summary>
    Task<bool> PingAsync(CancellationToken ct);

    /// <summary>Lights the indicator that points at the given room.</summary>
    Task ShowDirectionAsync(byte signalId, CancellationToken ct);

    /// <summary>Returns the column to its idle/attract state.</summary>
    Task ClearAsync(CancellationToken ct);

    /// <summary>
    /// Raised when the device signals it has something to report over the
    /// attention line. An i2c slave cannot start a conversation, so the ESP32
    /// pulls a GPIO low and we read in response.
    /// </summary>
    event EventHandler<DeviceEvent>? DeviceEventReceived;
}

/// <summary>Something the ESP32 reported upwards.</summary>
public sealed record DeviceEvent
{
    public required byte Code { get; init; }

    public required ReadOnlyMemory<byte> Payload { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }
}
