using Microsoft.Extensions.Logging;
using Zuil.Core.Abstractions;
using Zuil.Core.Devices;

namespace Zuil.Device;

/// <summary>
/// Logs instead of touching hardware, so the kiosk runs end-to-end on a laptop.
/// Selected with --fake-device, and the default whenever no i2c bus is present.
///
/// It answers from the catalogue rather than from a list of its own, so a newly
/// declared command is exercisable in the dev loop — including from the admin
/// screen — before any firmware exists for it.
/// </summary>
public sealed class FakeBridge : IDeviceBridge
{
    private readonly ILogger<FakeBridge> _logger;

    public FakeBridge(ILogger<FakeBridge> logger) => _logger = logger;

    public bool IsConnected => true;

    public event EventHandler<DeviceEvent>? DeviceEventReceived;

    public Task<DeviceReply?> SendAsync(DeviceInstruction instruction, CancellationToken ct)
    {
        var payload = Convert.ToHexString(instruction.Payload.Span);

        _logger.LogInformation(
            "[fake device] {Command} {Payload}",
            instruction.Capability.Key,
            payload.Length == 0 ? "(no payload)" : payload);

        if (!instruction.ExpectsReply)
        {
            return Task.FromResult<DeviceReply?>(null);
        }

        // PING answers PONG, everything else ACKs with the payload echoed back,
        // which is what the real firmware does and what the admin screen shows.
        var code = instruction.Command is DeviceCommand.Ping
            ? (byte)DeviceResponse.Pong
            : (byte)DeviceResponse.Ack;

        return Task.FromResult<DeviceReply?>(new DeviceReply
        {
            Code = code,
            Payload = instruction.Payload,
            ReceivedAt = DateTimeOffset.UtcNow,
            RoundTrip = TimeSpan.Zero,
        });
    }

    /// <summary>Lets tests push a synthetic device event through the pipeline.</summary>
    public void RaiseEvent(DeviceEvent evt) => DeviceEventReceived?.Invoke(this, evt);
}
