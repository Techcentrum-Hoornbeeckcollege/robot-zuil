namespace Zuil.Core.Models;

/// <summary>How well the kiosk is doing its job, rolled up to one value.</summary>
public enum HealthLevel
{
    /// <summary>Serving fresh data, clock trustworthy, column answering.</summary>
    Ok,

    /// <summary>Still useful to a visitor, but something needs attention.</summary>
    Degraded,

    /// <summary>Cannot answer the question it exists to answer.</summary>
    Down,
}

/// <summary>
/// The one place that decides what "healthy" means, so the public health
/// endpoint, the admin panel and the dot on the kiosk never disagree.
///
/// The thresholds are a judgement about a wayfinding kiosk rather than about a
/// web service, and they are deliberately asymmetric:
///
///  * A wrong clock is <see cref="HealthLevel.Down"/>. "Which meeting is on
///    now" is meaningless until the clock is trustworthy, and a Pi has no RTC —
///    so a confidently wrong answer sends a visitor to the wrong room, which is
///    worse than no answer.
///  * Never having synced is <see cref="HealthLevel.Down"/>: there is nothing to
///    search.
///  * A stale cache is only <see cref="HealthLevel.Degraded"/>. The kiosk always
///    serves from cache by design and warns the visitor itself; an hour-old room
///    assignment is usually still correct.
///  * A silent column is only <see cref="HealthLevel.Degraded"/>. The screen can
///    still name the room and the walking directions; it just cannot light the
///    indicator.
/// </summary>
public static class HealthEvaluation
{
    /// <param name="clockSynchronized">
    /// From <c>IClock</c>, not from <paramref name="status"/>: the sync status is
    /// what the database remembers about the last Graph call, and whether the OS
    /// clock has since been confirmed against NTP is not part of that record.
    /// </param>
    public static HealthLevel Evaluate(
        SyncStatus status,
        bool clockSynchronized,
        bool deviceConnected,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        if (!clockSynchronized || status.LastSuccessfulSync is null)
        {
            return HealthLevel.Down;
        }

        return status.IsStale(now, staleAfter) || !deviceConnected
            ? HealthLevel.Degraded
            : HealthLevel.Ok;
    }
}
