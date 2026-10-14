namespace Zuil.Core.Devices;

/// <summary>
/// The constants and codes the host and the firmware both have to agree on.
///
/// These live in Core rather than in Zuil.Device because the catalogue, the
/// admin API and the wire format all need them, and three copies of a command
/// byte is how a protocol drifts. See schema/device-protocol.md.
/// </summary>
public static class DeviceProtocol
{
    /// <summary>
    /// Payload bytes a single frame can carry. The firmware's receive buffer is
    /// the real limit; the catalogue refuses to declare a command that would
    /// overrun it, so a new parameter cannot silently truncate on the bus.
    /// </summary>
    public const int MaxPayload = 16;
}

/// <summary>Command byte values, host to device. Keep in sync with protocol.h.</summary>
public enum DeviceCommand : byte
{
    Ping = 0x01,
    ShowDirection = 0x02,
    Clear = 0x03,
    ReadEvent = 0x04,
}

/// <summary>Response and event codes the firmware may send back.</summary>
public enum DeviceResponse : byte
{
    Pong = 0x81,
    Ack = 0x82,
    Nack = 0x83,
    NoEvent = 0x84,
    Fault = 0x85,
}

/// <summary>Turns a reply code into something a maintainer can read on screen.</summary>
public static class DeviceResponseNames
{
    public static string Describe(byte code) => code switch
    {
        (byte)DeviceResponse.Pong => "PONG",
        (byte)DeviceResponse.Ack => "ACK",
        (byte)DeviceResponse.Nack => "NACK",
        (byte)DeviceResponse.NoEvent => "NO_EVENT",
        (byte)DeviceResponse.Fault => "FAULT",

        // An unknown code is worth showing rather than hiding: it usually means
        // the firmware is newer than this binary.
        _ => $"0x{code:X2}",
    };

    /// <summary>Whether the device actually applied the command.</summary>
    public static bool IsSuccess(byte code) =>
        code is (byte)DeviceResponse.Pong or (byte)DeviceResponse.Ack;
}
