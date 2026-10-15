using System.Reflection;
using System.Text.Json;
using Zuil.Core.Abstractions;
using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Host.Settings;

/// <summary>
/// Puts a starter set of rooms in on the very first run.
///
/// The list is an embedded resource rather than a file beside the binary,
/// because after 0.3 there are no files beside the binary — that is the whole
/// point of the release. It exists so a kiosk that has just been plugged in has
/// something on screen while someone sets it up, not as a way to configure
/// anything: edit the rooms under Instellingen → Zalen.
///
/// Guarded by a marker row rather than by "is the room table empty", so an
/// administrator who deliberately deletes every room does not find them back on
/// the next restart.
/// </summary>
public static class FirstRunSeeder
{
    private const string ResourceName = "Zuil.Host.Settings.default-rooms.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task SeedAsync(ISettingsStore store, ILogger logger, CancellationToken ct)
    {
        if (await store.IsSeededAsync(ct))
        {
            return;
        }

        var rooms = ReadDefaults(logger);

        if (rooms.Count > 0)
        {
            // Validate what we are about to import, but do not refuse to start
            // over it: the kiosk is more useful with an imperfect room list than
            // with none, and the panel is where it gets fixed.
            foreach (var error in SettingsValidator.ValidateRooms(rooms))
            {
                logger.LogWarning(
                    "Default room list is questionable at {Field}: {Message}",
                    error.Field, error.Message);
            }

            await store.SaveRoomsAsync(rooms, ct);
            logger.LogInformation("Seeded {Count} example rooms", rooms.Count);
        }

        await store.MarkSeededAsync(ct);
    }

    private static IReadOnlyList<Room> ReadDefaults(ILogger logger)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            logger.LogWarning(
                "No embedded default room list; starting with none. Add rooms under "
                + "Instellingen → Zalen.");
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Room>>(stream, Options) ?? [];
        }
        catch (JsonException ex)
        {
            // A broken resource is a build problem, not something an operator
            // can fix, and it must not stop the kiosk starting.
            logger.LogError(ex, "The embedded default room list could not be read");
            return [];
        }
    }
}
