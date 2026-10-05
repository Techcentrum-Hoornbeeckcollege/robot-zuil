using Microsoft.Extensions.Logging;
using Zuil.Core.Abstractions;

namespace Zuil.Device;

/// <summary>
/// Logs instead of touching hardware, so the kiosk runs end-to-end on a laptop.
/// Selected with --fake-device, and the default whenever no i2c bus is present.
/// </summary>
public sealed class FakeBridge : IDeviceBridge
{
    private readonly ILogger<FakeBridge> _logger;

    public FakeBridge(ILogger<FakeBridge> logger) => _logger = logger;

    public bool IsConnected => true;

    public event EventHandler<DeviceEvent>? DeviceEventReceived;

    public Task<bool> PingAsync(CancellationToken ct)
    {
        _logger.LogInformation("[fake device] ping");
        return Task.FromResult(true);
    }

    public Task ShowDirectionAsync(byte signalId, CancellationToken ct)
    {
        _logger.LogInformation("[fake device] show direction signal {SignalId}", signalId);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct)
    {
        _logger.LogInformation("[fake device] clear");
        return Task.CompletedTask;
    }

    /// <summary>Lets tests push a synthetic device event through the pipeline.</summary>
    public void RaiseEvent(DeviceEvent evt) => DeviceEventReceived?.Invoke(this, evt);
}
