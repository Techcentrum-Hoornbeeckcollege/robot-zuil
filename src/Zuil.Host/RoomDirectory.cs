using System.Text.Json;
using Zuil.Core.Abstractions;
using Zuil.Core.Models;

namespace Zuil.Host;

/// <summary>
/// Loads the six rooms from config/rooms.json at startup.
///
/// Kept in a file beside the binary rather than compiled in, because room names,
/// floors and walking directions change with the building, and that must not
/// require a rebuild and redeploy.
/// </summary>
public sealed class RoomDirectory : IRoomDirectory
{
    private readonly Dictionary<string, Room> _byId;
    private readonly Dictionary<string, Room> _byMailbox;

    public RoomDirectory(IReadOnlyList<Room> rooms)
    {
        All = rooms;
        _byId = rooms.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        _byMailbox = rooms.ToDictionary(r => r.Mailbox, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Room> All { get; }

    public Room? ById(string id) => _byId.GetValueOrDefault(id);

    public Room? ByMailbox(string mailbox) => _byMailbox.GetValueOrDefault(mailbox);

    public static RoomDirectory LoadFromFile(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Room configuration not found at '{path}'. Copy config/rooms.json next to the binary.",
                path);
        }

        var json = File.ReadAllText(path);
        var rooms = JsonSerializer.Deserialize<List<Room>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? [];

        if (rooms.Count == 0)
        {
            throw new InvalidOperationException($"No rooms configured in '{path}'.");
        }

        var duplicateSignals = rooms
            .GroupBy(r => r.SignalId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateSignals.Count > 0)
        {
            // Two rooms sharing an indicator means the column points at the wrong
            // place half the time — fail loudly at startup instead.
            throw new InvalidOperationException(
                $"Duplicate SignalId(s) in room config: {string.Join(", ", duplicateSignals)}");
        }

        logger.LogInformation("Loaded {Count} rooms from {Path}", rooms.Count, path);
        return new RoomDirectory(rooms);
    }
}
