namespace Zuil.Core.Models;

/// <summary>What the kiosk shows after a successful lookup.</summary>
public sealed record DirectionHint
{
    public required string RoomDisplayName { get; init; }

    public required string Floor { get; init; }

    public required string Directions { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required DateTimeOffset End { get; init; }

    /// <summary>Indicator to light on the physical column.</summary>
    public byte SignalId { get; init; }
}
