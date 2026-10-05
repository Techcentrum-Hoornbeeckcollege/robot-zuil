namespace Zuil.Core.Models;

/// <summary>
/// A meeting room the kiosk can route visitors to. Loaded from config/rooms.json,
/// not from Graph: Graph knows the mailbox, but only we know which way to walk.
/// </summary>
public sealed record Room
{
    /// <summary>Stable internal id, used in URLs and device commands.</summary>
    public required string Id { get; init; }

    /// <summary>Room resource mailbox, e.g. "room-2-14@contoso.com".</summary>
    public required string Mailbox { get; init; }

    /// <summary>Name shown to visitors, e.g. "Vergaderzaal 2.14".</summary>
    public required string DisplayName { get; init; }

    public required string Floor { get; init; }

    /// <summary>Human-readable walking directions shown on the kiosk.</summary>
    public required string Directions { get; init; }

    /// <summary>
    /// Which indicator on the ESP32 points at this room. Meaning depends on the
    /// physical build; see schema/device-protocol.md.
    /// </summary>
    public byte SignalId { get; init; }
}
