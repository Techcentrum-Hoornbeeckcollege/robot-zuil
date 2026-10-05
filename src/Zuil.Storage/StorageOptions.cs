namespace Zuil.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// SQLite file path. Relative paths resolve next to the executable, which on
    /// the Pi means alongside the single binary in /opt/zuil.
    /// </summary>
    public string DatabasePath { get; set; } = "zuil-cache.db";

    /// <summary>
    /// How long a cached meeting is kept after it ends. Short on purpose: this is
    /// attendee data on a machine in a public building, so there is no reason to
    /// retain yesterday's.
    /// </summary>
    public TimeSpan RetainPastMeetingsFor { get; set; } = TimeSpan.FromHours(2);
}
