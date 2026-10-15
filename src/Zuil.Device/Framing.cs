using Zuil.Core.Devices;

namespace Zuil.Device;

/// <summary>
/// Wire format shared with the ESP32 firmware. Must stay byte-for-byte in step
/// with firmware/esp32/include/protocol.h — see schema/device-protocol.md.
///
/// Layout: [len][cmd][payload 0..n][crc8]
/// where len counts cmd + payload + crc, and crc covers [len][cmd][payload].
/// i2c gives no message boundaries, so the length byte is what lets the firmware
/// know when a command is complete.
/// </summary>
public static class Framing
{
    /// <summary>Taken from Core so the catalogue and the wire agree by construction.</summary>
    public const int MaxPayload = DeviceProtocol.MaxPayload;
    public const int MaxFrame = 2 + MaxPayload + 1;

    public static byte[] Encode(DeviceCommand command, ReadOnlySpan<byte> payload) =>
        Encode((byte)command, payload);

    public static byte[] Encode(byte command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayload)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload), $"Payload exceeds {MaxPayload} bytes.");
        }

        var frame = new byte[2 + payload.Length + 1];
        frame[0] = (byte)(payload.Length + 2); // cmd + payload + crc
        frame[1] = command;
        payload.CopyTo(frame.AsSpan(2));
        frame[^1] = Crc8.Compute(frame.AsSpan(0, frame.Length - 1));

        return frame;
    }

    /// <summary>
    /// Validates a frame read back from the device. Returns false on a bad length
    /// or CRC so the caller can retry rather than act on garbage.
    /// </summary>
    public static bool TryDecode(
        ReadOnlySpan<byte> buffer,
        out byte code,
        out ReadOnlyMemory<byte> payload)
    {
        code = 0;
        payload = ReadOnlyMemory<byte>.Empty;

        if (buffer.Length < 3)
        {
            return false;
        }

        var declared = buffer[0];
        if (declared < 2 || declared > MaxPayload + 2 || declared + 1 > buffer.Length)
        {
            return false;
        }

        var frame = buffer[..(declared + 1)];
        var expected = Crc8.Compute(frame[..^1]);
        if (expected != frame[^1])
        {
            return false;
        }

        code = frame[1];
        payload = frame[2..^1].ToArray();
        return true;
    }
}
