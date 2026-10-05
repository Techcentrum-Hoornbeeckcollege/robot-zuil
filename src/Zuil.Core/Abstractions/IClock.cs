namespace Zuil.Core.Abstractions;

/// <summary>
/// Injectable clock. Everything here is time-dependent ("which meeting is on
/// now"), so tests must be able to control time, and the host must be able to
/// report whether the OS clock is actually trustworthy yet.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }

    /// <summary>
    /// Whether the OS clock has been synchronised against NTP. On a Raspberry Pi
    /// without an RTC this is false for the first seconds after boot, and false
    /// indefinitely if the network never comes up.
    /// </summary>
    bool IsSynchronized { get; }
}
