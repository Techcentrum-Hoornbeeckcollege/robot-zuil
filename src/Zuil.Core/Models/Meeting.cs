namespace Zuil.Core.Models;

/// <summary>
/// One occurrence of a meeting in one room. Recurring series are already
/// expanded into individual occurrences by the time they reach here.
/// </summary>
public sealed record Meeting
{
    /// <summary>Graph event id of this occurrence. Unique per room.</summary>
    public required string Id { get; init; }

    public required string RoomId { get; init; }

    /// <summary>
    /// Meeting subject. Treat as sensitive: subjects leak a lot ("1:1 re
    /// termination"), so the kiosk does not display this by default.
    /// </summary>
    public string? Subject { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required DateTimeOffset End { get; init; }

    public required IReadOnlyList<Attendee> Attendees { get; init; }

    public bool IsCancelled { get; init; }

    public bool Covers(DateTimeOffset at) => at >= Start && at < End;
}
