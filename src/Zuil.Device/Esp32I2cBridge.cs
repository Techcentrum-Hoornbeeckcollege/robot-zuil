using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Device.Gpio;
using System.Device.I2c;
using Zuil.Core.Abstractions;

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
    private readonly DeviceOptions _options;
    private readonly CommandPolicy _policy;
    private readonly ILogger<Esp32I2cBridge> _logger;

    private readonly I2cDevice _device;
    private readonly GpioController? _gpio;

    // The i2c bus is a single shared resource; concurrent transfers interleave
    // bytes and corrupt both messages.
    private readonly SemaphoreSlim _busLock = new(1, 1);

    public Esp32I2cBridge(
        IOptions<DeviceOptions> options,
        CommandPolicy policy,
        ILogger<Esp32I2cBridge> logger)
    {
        _options = options.Value;
        _policy = policy;
        _logger = logger;

        _device = I2cDevice.Create(new I2cConnectionSettings(_options.BusId, _options.Address));

        if (_options.AttentionPin is { } pin)
        {
            _gpio = new GpioController();
            _gpio.OpenPin(pin, PinMode.InputPullUp);
            _gpio.RegisterCallbackForPinValueChangedEvent(
                pin, PinEventTypes.Falling, OnAttentionAsserted);
        }
    }

    public bool IsConnected { get; private set; }

    public event EventHandler<DeviceEvent>? DeviceEventReceived;

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        var response = await TransactAsync(DeviceCommand.Ping, [], expectResponse: true, ct);
        IsConnected = response is { Code: (byte)DeviceResponse.Pong };
        return IsConnected;
    }

    public async Task ShowDirectionAsync(byte signalId, CancellationToken ct) =>
        await TransactAsync(
            DeviceCommand.ShowDirection, [signalId], expectResponse: true, ct);

    public async Task ClearAsync(CancellationToken ct) =>
        await TransactAsync(DeviceCommand.Clear, [], expectResponse: true, ct);

    private async Task<DeviceEvent?> TransactAsync(
        DeviceCommand command,
        byte[] payload,
        bool expectResponse,
        CancellationToken ct)
    {
        if (!_policy.IsAllowed(command))
        {
            throw new InvalidOperationException($"Command {command} is not allowlisted.");
        }

        if (!_policy.TryAcquire(DateTimeOffset.UtcNow))
        {
            _logger.LogWarning("Device command {Command} dropped by rate limit", command);
            return null;
        }

        var frame = Framing.Encode(command, payload);

        await _busLock.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= _options.MaxRetries; attempt++)
            {
                try
                {
                    _device.Write(frame);

                    if (!expectResponse)
                    {
                        return null;
                    }

                    // Give the slave time to prepare its reply.
                    await Task.Delay(_options.ResponseDelay, ct);

                    var buffer = new byte[Framing.MaxFrame];
                    _device.Read(buffer);

                    if (Framing.TryDecode(buffer, out var code, out var responsePayload))
                    {
                        return new DeviceEvent
                        {
                            Code = code,
                            Payload = responsePayload,
                            ReceivedAt = DateTimeOffset.UtcNow,
                        };
                    }

                    _logger.LogWarning(
                        "Bad frame from device on attempt {Attempt}/{Max} for {Command}",
                        attempt, _options.MaxRetries, command);
                }
                catch (IOException ex) when (attempt < _options.MaxRetries)
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
                var evt = await TransactAsync(
                    DeviceCommand.ReadEvent, [], expectResponse: true, CancellationToken.None);

                if (evt is not null && evt.Code != (byte)DeviceResponse.NoEvent)
                {
                    DeviceEventReceived?.Invoke(this, evt);
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
        if (_options.AttentionPin is { } pin && _gpio is not null)
        {
            _gpio.UnregisterCallbackForPinValueChangedEvent(pin, OnAttentionAsserted);
            _gpio.Dispose();
        }

        _busLock.Dispose();
        _device.Dispose();
    }
}
