using System.Text.Json;
using Zuil.Core.Models;

namespace Zuil.Host.Settings;

/// <summary>
/// Reads config/rooms.json.
///
/// Since the rooms moved into the database this file is only a first-run seed and
/// a paste-back target for the admin panel's export — editing it on a running
/// kiosk does nothing, which config/README.md says in as many words.
/// </summary>
public static class RoomsFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        // rooms.json ships with explanatory comments, and losing them to a
        // parser that cannot skip them would make it much less useful.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The rooms in <paramref name="path"/>, or an empty list if the file is not
    /// there. Absent is not an error: a fresh install must boot with no rooms and
    /// let an administrator add them from the GUI.
    /// </summary>
    public static IReadOnlyList<Room> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<Room>>(json, Options) ?? [];
    }
}
