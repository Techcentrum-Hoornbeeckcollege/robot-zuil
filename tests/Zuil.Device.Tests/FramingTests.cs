using Zuil.Device;

namespace Zuil.Device.Tests;

/// <summary>
/// Framing is implemented twice — here and in firmware/esp32/include/protocol.h.
/// These tests are the fixed reference both must match; if you change one side,
/// the expected bytes below tell you what the other has to produce.
/// </summary>
public class FramingTests
{
    [Fact]
    public void Encodes_length_command_and_crc()
    {
        var frame = Framing.Encode(DeviceCommand.ShowDirection, [0x03]);

        Assert.Equal(4, frame.Length);
        Assert.Equal(3, frame[0]); // cmd + payload + crc
        Assert.Equal((byte)DeviceCommand.ShowDirection, frame[1]);
        Assert.Equal(0x03, frame[2]);
        Assert.Equal(Crc8.Compute(frame.AsSpan(0, 3)), frame[3]);
    }

    [Fact]
    public void Round_trips_a_valid_frame()
    {
        var frame = Framing.Encode(DeviceCommand.Ping, []);

        Assert.True(Framing.TryDecode(frame, out var code, out var payload));
        Assert.Equal((byte)DeviceCommand.Ping, code);
        Assert.Empty(payload.ToArray());
    }

    [Fact]
    public void Rejects_a_corrupted_payload()
    {
        // The whole point of the CRC: a single flipped bit on the bus must not be
        // acted on, because a corrupted signal id sends a visitor to the wrong room.
        var frame = Framing.Encode(DeviceCommand.ShowDirection, [0x03]);
        frame[2] ^= 0x01;

        Assert.False(Framing.TryDecode(frame, out _, out _));
    }

    [Fact]
    public void Rejects_a_truncated_frame() =>
        Assert.False(Framing.TryDecode([0x05, 0x02], out _, out _));

    [Fact]
    public void Rejects_an_oversized_payload() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Framing.Encode(DeviceCommand.ShowDirection, new byte[Framing.MaxPayload + 1]));
}
