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

    // There is deliberately no ClockSynchronized here. Whether the OS clock has
    // been confirmed against NTP is not something the last Graph call recorded,
    // so it would always have read false; IClock.IsSynchronized is the live
    // answer and HealthEvaluation takes it as its own argument.

    public bool IsStale(DateTimeOffset now, TimeSpan tolerance) =>
        LastSuccessfulSync is null || now - LastSuccessfulSync > tolerance;
}
