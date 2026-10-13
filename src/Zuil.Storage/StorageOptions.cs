namespace Zuil.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// SQLite file path. Relative paths resolve next to the executable, which on
    /// the Pi means alongside the single binary in /opt/zuil.
    /// </summary>
    public string DatabasePath { get; set; } = "zuil-cache.db";

    // Retention moved to Sync:RetainPastMeetingsFor in the database, where the
    // admin panel can edit it. FirstRunSeeder still reads the old
    // Storage:RetainPastMeetingsFor key once, to carry an existing install
    // forward, which is why that key is not simply forgotten.
}
