using Zuil.Core.Devices;

namespace Zuil.Core.Abstractions;

/// <summary>
/// Talks to the ESP32 that drives the physical wayfinding indicators.
///
/// One method, deliberately. The commands are described as data in
/// <see cref="DeviceCatalogue"/>, so teaching the kiosk a new one — a height, a
/// colour, an animation — does not add a method here, which means it does not
/// touch the i2c implementation, the fake, or the rate limiter either.
///
/// The transport also lives entirely behind this interface. The default
/// implementation is i2c with the Pi as master, but ESP32 i2c-slave support is
/// the fragile part of that design — if it misbehaves on the bench, a UART
/// implementation drops in here without touching anything else.
/// </summary>
public interface IDeviceBridge
{
    /// <summary>True while the device is answering.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Sends one validated instruction and returns what came back, or null when
    /// the command was dropped (rate limit) or never acknowledged (bus failure
    /// after retries). A null is a normal outcome on a wall-mounted machine, not
    /// an exception: the kiosk keeps serving with a dark column.
    /// </summary>
    Task<DeviceReply?> SendAsync(DeviceInstruction instruction, CancellationToken ct);

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
