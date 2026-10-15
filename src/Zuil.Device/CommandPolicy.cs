using Zuil.Core.Abstractions;
using Zuil.Core.Devices;

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
    private readonly ISettingsProvider _settings;
    private readonly Lock _gate = new();
    private readonly Queue<DateTimeOffset> _recent = new();

    public CommandPolicy(ISettingsProvider settings) => _settings = settings;

    /// <summary>
    /// Only commands the catalogue declares reach the bus. The allowlist used
    /// to be a second copy of that list here, which is one list too many: the
    /// catalogue is what the GUI and the API already work from.
    /// </summary>
    public bool IsAllowed(DeviceCommand command) => DeviceCatalogue.IsDeclared(command);

    public bool TryAcquire(DateTimeOffset now)
    {
        // Read inside the lock rather than cached in the constructor, so raising
        // the limit in the admin panel takes effect on the next command.
        var maxPerSecond = Math.Max(1, _settings.Current.Device.MaxCommandsPerSecond);

        lock (_gate)
        {
            while (_recent.Count > 0 && now - _recent.Peek() > TimeSpan.FromSeconds(1))
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= maxPerSecond)
            {
                return false;
            }

            _recent.Enqueue(now);
            return true;
        }
    }
}
