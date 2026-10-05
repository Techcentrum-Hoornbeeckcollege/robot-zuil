namespace Zuil.Core.Models;

/// <summary>
/// A person on a meeting. Stored so the kiosk can answer "which room is my
/// meeting in" by name. Never rendered as a list; see docs/privacy.md.
/// </summary>
public sealed record Attendee
{
    public required string Name { get; init; }

    /// <summary>
    /// Lowercased, accent-folded, whitespace-collapsed form of <see cref="Name"/>,
    /// used for exact-match lookup. Set by the storage layer.
    /// </summary>
    public required string NormalizedName { get; init; }
}
