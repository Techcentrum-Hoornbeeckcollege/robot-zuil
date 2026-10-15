using Microsoft.Extensions.Logging;
using System.Device.Gpio;
using System.Device.I2c;
using System.Diagnostics;
using Zuil.Core.Abstractions;
using Zuil.Core.Devices;
using Zuil.Core.Settings;

namespace Zuil.Device;

/// <summary>
/// Drives the ESP32 over i2c with the Pi as bus master.
///
/// Two constraints shape this class:
///  * The ESP32 is the slave, and ESP32 i2c-slave support is the weak spot of
///    that peripheral — clock stretching is unreliable, so we write, pause, then
///    read rather than expecting the slave to hold the bus while it thinks.
///  * A slave cannot start a transfer, so unsolicited reports arrive as an edge
///    on the attention GPIO, which we answer with a ReadEvent command.
///
/// If this proves flaky on the bench, swap in a UART implementation of
/// IDeviceBridge; nothing above this layer knows which transport is in use.
/// </summary>
public sealed class Esp32I2cBridge : IDeviceBridge, IDisposable
{
    // The bus, the address and the attention pin as they were at startup.
    // Re-opening an I2cDevice and re-registering GPIO callbacks while the kiosk
    // is serving is a new failure mode on a wall-mounted machine for near-zero
    // benefit, so those three need a restart; the admin panel badges them as
    // such. Timing and retries are read live, below.
    private readonly DeviceSettings _startup;

    private readonly ISettingsProvider _settings;
    private readonly CommandPolicy _policy;
    private readonly ILogger<Esp32I2cBridge> _logger;

    private readonly I2cDevice _device;
    private readonly GpioController? _gpio;

    // The i2c bus is a single shared resource; concurrent transfers interleave
    // bytes and corrupt both messages.
    private readonly SemaphoreSlim _busLock = new(1, 1);

    public Esp32I2cBridge(
        ISettingsProvider settings,
        CommandPolicy policy,
        ILogger<Esp32I2cBridge> logger)
    {
        _settings = settings;
        _startup = settings.Current.Device;
        _policy = policy;
        _logger = logger;

        _device = I2cDevice.Create(new I2cConnectionSettings(_startup.BusId, _startup.Address));

        if (_startup.AttentionPin is { } pin)
        {
            _gpio = new GpioController();
            _gpio.OpenPin(pin, PinMode.InputPullUp);
            _gpio.RegisterCallbackForPinValueChangedEvent(
                pin, PinEventTypes.Falling, OnAttentionAsserted);
        }
    }

    public bool IsConnected { get; private set; }

    public event EventHandler<DeviceEvent>? DeviceEventReceived;

    /// <summary>
    /// The single transfer. Everything named — ping, show direction, clear —
    /// arrives here as an already-validated instruction, so this method is only
    /// concerned with the bus: allowlist, rate limit, frame, write, wait, read,
    /// retry.
    /// </summary>
    public async Task<DeviceReply?> SendAsync(
        DeviceInstruction instruction,
        CancellationToken ct)
    {
        var command = instruction.Command;

        if (!_policy.IsAllowed(command))
        {
            throw new InvalidOperationException($"Command {command} is not allowlisted.");
        }

        if (!_policy.TryAcquire(DateTimeOffset.UtcNow))
        {
            _logger.LogWarning("Device command {Command} dropped by rate limit", command);
            return null;
        }

        var frame = Framing.Encode((byte)command, instruction.Payload.Span);

        // One snapshot per transfer: an administrator raising the retry count
        // mid-transfer must not change the bound the loop is already running to.
        var device = _settings.Current.Device;
        var maxRetries = Math.Max(1, device.MaxRetries);
        var started = Stopwatch.GetTimestamp();

        await _busLock.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _device.Write(frame);

                    if (!instruction.ExpectsReply)
                    {
                        return null;
                    }

                    // Give the slave time to prepare its reply.
                    await Task.Delay(device.ResponseDelay, ct);

                    var buffer = new byte[Framing.MaxFrame];
                    _device.Read(buffer);

                    if (Framing.TryDecode(buffer, out var code, out var responsePayload))
                    {
                        // Any decodable reply proves the link works, whatever the
                        // firmware thought of the command itself.
                        IsConnected = true;

                        return new DeviceReply
                        {
                            Code = code,
                            Payload = responsePayload,
                            ReceivedAt = DateTimeOffset.UtcNow,
                            RoundTrip = Stopwatch.GetElapsedTime(started),
                            Attempts = attempt,
                        };
                    }

                    _logger.LogWarning(
                        "Bad frame from device on attempt {Attempt}/{Max} for {Command}",
                        attempt, maxRetries, command);
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    // Typical on an ESP32 slave under load: NACK or bus error.
                    _logger.LogWarning(
                        ex, "i2c transfer failed on attempt {Attempt}, retrying", attempt);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), ct);
            }

            IsConnected = false;
            _logger.LogError("Device command {Command} failed after retries", command);
            return null;
        }
        finally
        {
            _busLock.Release();
        }
    }

    private void OnAttentionAsserted(object sender, PinValueChangedEventArgs args)
    {
        // Fire and forget: this runs on the GPIO callback thread, which must not
        // block on an i2c transfer.
        _ = Task.Run(async () =>
        {
            try
            {
                var reply = await this.ReadEventAsync(CancellationToken.None);

                if (reply is not null && reply.Code != (byte)DeviceResponse.NoEvent)
                {
                    DeviceEventReceived?.Invoke(this, new DeviceEvent
                    {
                        Code = reply.Code,
                        Payload = reply.Payload,
                        ReceivedAt = reply.ReceivedAt,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read device event");
            }
        });
    }

    public void Dispose()
    {
        if (_startup.AttentionPin is { } pin && _gpio is not null)
        {
            _gpio.UnregisterCallbackForPinValueChangedEvent(pin, OnAttentionAsserted);
            _gpio.Dispose();
        }

        _busLock.Dispose();
        _device.Dispose();
    }
}
