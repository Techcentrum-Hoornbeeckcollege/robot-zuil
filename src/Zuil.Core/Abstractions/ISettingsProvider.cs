using Zuil.Core.Settings;

namespace Zuil.Core.Abstractions;

/// <summary>
/// Hands out the current <see cref="KioskSettings"/> and says when they change.
///
/// Lives in Core rather than beside its implementation in the host, because the
/// Graph, storage and device layers all need to read settings an administrator
/// may have changed a second ago, and none of them may reference the host.
/// </summary>
public interface ISettingsProvider
{
    /// <summary>
    /// The settings as of now: a reference to an immutable snapshot. A caller
    /// that reads this once and uses it for the whole of an operation is
    /// guaranteed a consistent view, which is how the sync worker avoids mixing
    /// two room lists into one cache swap.
    /// </summary>
    KioskSettings Current { get; }

    /// <summary>
    /// Raised after new settings have been swapped in. Subscribers rebuild
    /// derived state: the room directory, the MSAL client, the sync timer.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>Re-reads from the store and raises <see cref="Changed"/>.</summary>
    Task ReloadAsync(CancellationToken ct);
}
