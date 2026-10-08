using Zuil.Core.Abstractions;
using Zuil.Core.Settings;

namespace Zuil.Host.Settings;

/// <summary>
/// The live settings snapshot.
///
/// A plain volatile field rather than an IConfigurationSource over SQLite, which
/// was the other candidate. The configuration route looks like it would let the
/// existing IOptions plumbing live-reload for free, but it would not: every
/// consumer in this app captures options.Value in its constructor, so a reload
/// token firing changes nothing until each one is rewritten anyway. Given that
/// the call sites have to change either way, a typed snapshot with real
/// validation beats a string-keyed dictionary with none.
/// </summary>
public sealed class SettingsProvider : ISettingsProvider
{
    private readonly ISettingsStore _store;
    private readonly ILogger<SettingsProvider> _logger;

    private volatile KioskSettings _current = new();

    public SettingsProvider(ISettingsStore store, ILogger<SettingsProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    public KioskSettings Current => _current;

    public event EventHandler? Changed;

    public async Task ReloadAsync(CancellationToken ct)
    {
        _current = await _store.LoadAsync(ct);

        // One misbehaving subscriber must not stop the others from seeing the
        // change, or the kiosk ends up half-reconfigured.
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A settings-changed subscriber threw");
            }
        }
    }
}
