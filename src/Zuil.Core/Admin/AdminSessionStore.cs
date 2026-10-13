using System.Collections.Concurrent;
using Zuil.Core.Abstractions;

namespace Zuil.Core.Admin;

/// <summary>
/// Live admin sessions, keyed by a hash of the bearer token.
///
/// In-process and deliberately not persisted. Cookie authentication would need a
/// Data Protection key ring, and on the Pi systemd sets ProtectHome=true, which
/// makes Data Protection's default key home unwritable — it would silently fall
/// back to ephemeral keys and log everyone out on every restart anyway. An
/// opaque token against this table gets the same property with nothing to
/// misconfigure, and losing every session on restart is desirable for a machine
/// in a lobby.
///
/// Only the token's hash is stored, so a memory dump does not hand over a live
/// session.
/// </summary>
public sealed class AdminSessionStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSeen = new(StringComparer.Ordinal);
    private readonly IClock _clock;
    private readonly TimeSpan _idleTimeout;

    public AdminSessionStore(IClock clock, TimeSpan idleTimeout)
    {
        _clock = clock;
        _idleTimeout = idleTimeout;
    }

    /// <summary>Registers a freshly issued token.</summary>
    public void Create(string tokenHash) => _lastSeen[tokenHash] = _clock.Now;

    /// <summary>
    /// Whether this token is still good, bumping its last-seen if so. Sliding
    /// expiry: an administrator working through the settings is not logged out
    /// mid-edit, but a panel left open in a lobby goes stale on its own.
    /// </summary>
    public bool Validate(string tokenHash)
    {
        var now = _clock.Now;

        // Sweep on access rather than on a timer: there are never more than a
        // handful of entries, and a background timer is a thread for nothing.
        foreach (var (hash, seen) in _lastSeen)
        {
            if (now - seen > _idleTimeout)
            {
                _lastSeen.TryRemove(hash, out _);
            }
        }

        if (!_lastSeen.TryGetValue(tokenHash, out var lastSeen))
        {
            return false;
        }

        if (now - lastSeen > _idleTimeout)
        {
            _lastSeen.TryRemove(tokenHash, out _);
            return false;
        }

        _lastSeen[tokenHash] = now;
        return true;
    }

    /// <summary>Ends one session, on explicit logout or on admin idle.</summary>
    public void Remove(string tokenHash) => _lastSeen.TryRemove(tokenHash, out _);

    /// <summary>Ends every session, used when the password changes.</summary>
    public void Clear() => _lastSeen.Clear();

    /// <summary>How many sessions are live, for the diagnostics page.</summary>
    public int Count => _lastSeen.Count;
}
