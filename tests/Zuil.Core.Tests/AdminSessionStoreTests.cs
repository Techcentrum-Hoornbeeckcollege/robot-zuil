using Zuil.Core.Abstractions;
using Zuil.Core.Admin;

namespace Zuil.Core.Tests;

/// <summary>
/// A clock the test moves by hand, so expiry is tested without sleeping.
/// </summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    public bool IsSynchronized => true;

    public void Advance(TimeSpan by) => Now += by;
}

public class AdminSessionStoreTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

    /// <summary>The timeout is a live setting, so the store reads it per check.</summary>
    private static Func<TimeSpan> Timeout(TimeSpan value) => () => value;

    [Fact]
    public void Accepts_a_token_it_issued()
    {
        var store = new AdminSessionStore(new FakeClock(), Timeout(Idle));
        store.Create("hash");

        Assert.True(store.Validate("hash"));
    }

    [Fact]
    public void Rejects_a_token_it_never_issued()
    {
        var store = new AdminSessionStore(new FakeClock(), Timeout(Idle));

        Assert.False(store.Validate("made-up"));
    }

    [Fact]
    public void Expires_a_session_left_idle()
    {
        var clock = new FakeClock();
        var store = new AdminSessionStore(clock, Timeout(Idle));
        store.Create("hash");

        clock.Advance(Idle + TimeSpan.FromSeconds(1));

        Assert.False(store.Validate("hash"));
    }

    /// <summary>
    /// Sliding, so working through the settings for twenty minutes does not log
    /// you out mid-edit.
    /// </summary>
    [Fact]
    public void Keeps_an_active_session_alive_past_the_timeout()
    {
        var clock = new FakeClock();
        var store = new AdminSessionStore(clock, Timeout(Idle));
        store.Create("hash");

        for (var i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(10));
            Assert.True(store.Validate("hash"));
        }

        clock.Advance(Idle + TimeSpan.FromSeconds(1));
        Assert.False(store.Validate("hash"));
    }

    [Fact]
    public void Logout_ends_the_session_immediately()
    {
        var store = new AdminSessionStore(new FakeClock(), Timeout(Idle));
        store.Create("hash");

        store.Remove("hash");

        Assert.False(store.Validate("hash"));
    }

    [Fact]
    public void Clear_ends_every_session()
    {
        var store = new AdminSessionStore(new FakeClock(), Timeout(Idle));
        store.Create("one");
        store.Create("two");

        store.Clear();

        Assert.False(store.Validate("one"));
        Assert.False(store.Validate("two"));
        Assert.Equal(0, store.Count);
    }

    /// <summary>
    /// Expired entries are swept on access rather than by a timer, so they must
    /// not accumulate for the lifetime of a kiosk that runs for months.
    /// </summary>
    [Fact]
    public void Sweeps_expired_sessions_on_access()
    {
        var clock = new FakeClock();
        var store = new AdminSessionStore(clock, Timeout(Idle));

        store.Create("stale-one");
        store.Create("stale-two");

        clock.Advance(Idle + TimeSpan.FromMinutes(1));
        store.Create("fresh");

        Assert.True(store.Validate("fresh"));
        Assert.Equal(1, store.Count);
    }
}
