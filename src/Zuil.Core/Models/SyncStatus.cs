namespace Zuil.Core.Models;

/// <summary>
/// Health of the calendar cache. The kiosk serves from cache always, so it needs
/// to know how stale that cache is in order to warn visitors.
/// </summary>
public sealed record SyncStatus
{
    public DateTimeOffset? LastSuccessfulSync { get; init; }

    public DateTimeOffset? LastAttemptedSync { get; init; }

    public string? LastError { get; init; }

    public int MeetingCount { get; init; }

    /// <summary>
    /// False until the OS clock has been confirmed against NTP. A Pi with no RTC
    /// boots with a wrong clock, and "which meeting is on now" is meaningless
    /// until the clock is trustworthy.
    /// </summary>
    public bool ClockSynchronized { get; init; }

    public bool IsStale(DateTimeOffset now, TimeSpan tolerance) =>
        LastSuccessfulSync is null || now - LastSuccessfulSync > tolerance;
}
