using Zuil.Core.Models;

namespace Zuil.Core.Tests;

/// <summary>
/// One rule, read by the public health endpoint, the admin panel and the dot on
/// the kiosk. The thresholds are a judgement about a wayfinding kiosk, so they
/// are worth stating as tests: a confidently wrong answer is worse than a
/// visibly missing one.
/// </summary>
public class HealthEvaluationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    private static SyncStatus Synced(TimeSpan ago) => new()
    {
        LastSuccessfulSync = Now - ago,
        LastAttemptedSync = Now - ago,
        MeetingCount = 12,
    };

    private static HealthLevel Evaluate(
        SyncStatus status,
        bool clockSynchronized = true,
        bool deviceConnected = true) =>
        HealthEvaluation.Evaluate(status, clockSynchronized, deviceConnected, Now, StaleAfter);

    [Fact]
    public void Fresh_cache_live_column_and_a_good_clock_is_ok() =>
        Assert.Equal(HealthLevel.Ok, Evaluate(Synced(TimeSpan.FromMinutes(2))));

    /// <summary>
    /// The one that matters most. A Pi has no RTC, so an unsynchronised clock
    /// means "which meeting is on now" has no trustworthy answer — and the
    /// kiosk would send a visitor to the wrong room with full confidence.
    /// </summary>
    [Fact]
    public void An_unsynchronised_clock_is_down_however_fresh_the_cache_is() =>
        Assert.Equal(
            HealthLevel.Down,
            Evaluate(Synced(TimeSpan.Zero), clockSynchronized: false));

    [Fact]
    public void Never_having_synced_is_down_because_there_is_nothing_to_search() =>
        Assert.Equal(HealthLevel.Down, Evaluate(new SyncStatus()));

    /// <summary>
    /// Serving from cache is the design, not a fault: the kiosk warns the
    /// visitor itself and an hour-old room assignment is usually still right.
    /// </summary>
    [Fact]
    public void A_stale_cache_is_only_degraded() =>
        Assert.Equal(HealthLevel.Degraded, Evaluate(Synced(TimeSpan.FromHours(2))));

    /// <summary>
    /// The screen can still name the room and the way to walk; only the
    /// indicator is dark. That is not worth telling a visitor the kiosk is down.
    /// </summary>
    [Fact]
    public void A_silent_column_is_only_degraded() =>
        Assert.Equal(
            HealthLevel.Degraded,
            Evaluate(Synced(TimeSpan.FromMinutes(2)), deviceConnected: false));

    [Fact]
    public void Down_wins_over_degraded() =>
        Assert.Equal(
            HealthLevel.Down,
            Evaluate(
                Synced(TimeSpan.FromHours(2)),
                clockSynchronized: false,
                deviceConnected: false));

    /// <summary>The boundary is the configured tolerance, not a hardcoded one.</summary>
    [Theory]
    [InlineData(29, HealthLevel.Ok)]
    [InlineData(31, HealthLevel.Degraded)]
    public void Staleness_follows_the_configured_threshold(int minutesAgo, HealthLevel expected) =>
        Assert.Equal(expected, Evaluate(Synced(TimeSpan.FromMinutes(minutesAgo))));

    /// <summary>
    /// The strings the API sends and the GUI switches on. Renaming an enum
    /// member would otherwise silently stop matching the frontend's union type.
    /// </summary>
    [Theory]
    [InlineData(HealthLevel.Ok, "Ok")]
    [InlineData(HealthLevel.Degraded, "Degraded")]
    [InlineData(HealthLevel.Down, "Down")]
    public void Serialises_under_the_names_the_gui_expects(HealthLevel level, string expected) =>
        Assert.Equal(expected, level.ToString());
}
