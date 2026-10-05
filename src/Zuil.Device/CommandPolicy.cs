using Microsoft.Extensions.Options;

namespace Zuil.Device;

/// <summary>
/// Gate in front of the device bridge.
///
/// Device commands move physical hardware, and the HTTP surface that triggers
/// them is reachable from a touchscreen in a public lobby. So: only known
/// commands get through, and a visitor hammering the screen cannot turn into an
/// unbounded write loop on the i2c bus.
/// </summary>
public sealed class CommandPolicy
{
    private static readonly HashSet<DeviceCommand> Allowed =
    [
        DeviceCommand.Ping,
        DeviceCommand.ShowDirection,
        DeviceCommand.Clear,
        DeviceCommand.ReadEvent,
    ];

    private readonly int _maxPerSecond;
    private readonly Lock _gate = new();
    private readonly Queue<DateTimeOffset> _recent = new();

    public CommandPolicy(IOptions<DeviceOptions> options) =>
        _maxPerSecond = options.Value.MaxCommandsPerSecond;

    public bool IsAllowed(DeviceCommand command) => Allowed.Contains(command);

    public bool TryAcquire(DateTimeOffset now)
    {
        lock (_gate)
        {
            while (_recent.Count > 0 && now - _recent.Peek() > TimeSpan.FromSeconds(1))
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= _maxPerSecond)
            {
                return false;
            }

            _recent.Enqueue(now);
            return true;
        }
    }
}
