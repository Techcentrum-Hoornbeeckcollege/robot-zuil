using Zuil.Core.Abstractions;
using Zuil.Core.Models;

namespace Zuil.Host;

/// <summary>
/// The rooms this column serves, as a lock-free snapshot over the settings
/// provider.
///
/// Replaces the old RoomDirectory, which read config/rooms.json once at startup
/// and could never change. Room names, floors and walking directions are exactly
/// the settings most likely to need editing, and doing that over SSH on a Pi
/// bolted to a wall was the thing this whole change set out to fix.
///
/// The interface is unchanged, so every existing caller is untouched. Readers get
/// an immutable snapshot swapped in with a single volatile write, so a lookup
/// during a room edit sees either the old directory or the new one and never a
/// half-rebuilt dictionary.
/// </summary>
public sealed class SqliteBackedRoomDirectory : IRoomDirectory, IDisposable
{
    private readonly ISettingsProvider _settings;
    private readonly ILogger<SqliteBackedRoomDirectory> _logger;

    private volatile Snapshot _snapshot = Snapshot.Empty;

    public SqliteBackedRoomDirectory(
        ISettingsProvider settings,
        ILogger<SqliteBackedRoomDirectory> logger)
    {
        _settings = settings;
        _logger = logger;

        Rebuild();
        _settings.Changed += OnSettingsChanged;
    }

    public IReadOnlyList<Room> All => _snapshot.All;

    public Room? ById(string id) => _snapshot.ById.GetValueOrDefault(id);

    public Room? ByMailbox(string mailbox) => _snapshot.ByMailbox.GetValueOrDefault(mailbox);

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var rooms = _settings.Current.Rooms;

        var byId = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
        var byMailbox = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
        var usable = new List<Room>(rooms.Count);

        foreach (var room in rooms)
        {
            // The database has unique indexes and the admin API validates, so a
            // duplicate here means someone edited the file by hand. Keep the
            // first and log, rather than throwing: a kiosk that refuses to boot
            // cannot be repaired from the GUI that is now its only repair tool.
            // This is a deliberate reversal of the old fail-fast behaviour.
            if (!byId.TryAdd(room.Id, room))
            {
                _logger.LogError("Duplicate room id {RoomId}; ignoring the later one", room.Id);
                continue;
            }

            if (!byMailbox.TryAdd(room.Mailbox, room))
            {
                _logger.LogError(
                    "Room {RoomId} reuses mailbox {Mailbox}; ignoring it", room.Id, room.Mailbox);
                byId.Remove(room.Id);
                continue;
            }

            usable.Add(room);
        }

        _snapshot = new Snapshot(usable, byId, byMailbox);
        _logger.LogInformation("Room directory now has {Count} rooms", usable.Count);
    }

    /// <summary>
    /// One immutable view of the directory: the list and both lookups, swapped
    /// together so they can never disagree.
    /// </summary>
    private sealed record Snapshot(
        IReadOnlyList<Room> All,
        Dictionary<string, Room> ById,
        Dictionary<string, Room> ByMailbox)
    {
        public static readonly Snapshot Empty = new(
            [],
            new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase));
    }
}
