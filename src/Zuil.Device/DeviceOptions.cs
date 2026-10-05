namespace Zuil.Device;

public sealed class DeviceOptions
{
    public const string SectionName = "Device";

    /// <summary>I2c bus number. On a Raspberry Pi header this is bus 1.</summary>
    public int BusId { get; set; } = 1;

    /// <summary>7-bit slave address the ESP32 firmware registers.</summary>
    public int Address { get; set; } = 0x42;

    /// <summary>
    /// BCM pin on which the ESP32 asserts "I have something to report". An i2c
    /// slave cannot initiate a transfer, so without this line the Pi would have
    /// to poll the device continuously. Set to null to disable and poll instead.
    /// </summary>
    public int? AttentionPin { get; set; } = 17;

    /// <summary>
    /// How long to wait for the slave to have a response ready before reading it
    /// back. ESP32 i2c-slave handling is not instantaneous and it cannot stretch
    /// the clock reliably, so we give the firmware a moment rather than reading
    /// straight back and getting 0xFF.
    /// </summary>
    public TimeSpan ResponseDelay { get; set; } = TimeSpan.FromMilliseconds(5);

    public int MaxRetries { get; set; } = 3;

    /// <summary>Upper bound on device commands per second; see CommandPolicy.</summary>
    public int MaxCommandsPerSecond { get; set; } = 10;
}
