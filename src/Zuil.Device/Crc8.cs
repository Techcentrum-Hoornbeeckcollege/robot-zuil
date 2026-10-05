namespace Zuil.Device;

/// <summary>
/// CRC-8/ATM (polynomial 0x07, init 0x00) — the same algorithm the firmware
/// implements in include/protocol.h.
///
/// Needed because i2c has no integrity checking of its own: a glitch on a long
/// ribbon cable arrives as a perfectly valid-looking byte, and a corrupted
/// signal id would point a visitor at the wrong room.
/// </summary>
public static class Crc8
{
    public static byte Compute(ReadOnlySpan<byte> data)
    {
        byte crc = 0x00;

        foreach (var b in data)
        {
            crc ^= b;

            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x80) != 0
                    ? (byte)((crc << 1) ^ 0x07)
                    : (byte)(crc << 1);
            }
        }

        return crc;
    }
}
