using Zuil.Core.Abstractions;
using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Host.Settings;

/// <summary>
/// Moves an existing installation's configuration into the database, once.
///
/// This is what makes the admin panel a non-breaking upgrade: a Pi that already
/// has a populated config/appsettings.Production.json and config/rooms.json keeps
/// behaving identically after the update, because its old values are copied
/// across on the first boot — including its client secret, which gets encrypted
/// on the way in.
///
/// Guarded by a marker rather than by "is the room table empty", so an
/// administrator who deliberately deletes every room does not find them back
/// after the next restart.
/// </summary>
public static class FirstRunSeeder
{
    public static async Task SeedAsync(
        ISettingsStore store,
        IConfiguration configuration,
        string roomsFilePath,
        ILogger logger,
        CancellationToken ct)
    {
        if (await store.IsSeededAsync(ct))
        {
            return;
        }

        logger.LogInformation("First run: seeding settings from configuration files");

        var settings = ReadFromConfiguration(configuration);
        var rooms = RoomsFile.Read(roomsFilePath);

        // Validate what we are about to import, but do not refuse to start over
        // it: the old config may well have a problem the admin panel is now the
        // right place to fix. Importing it and complaining beats refusing to boot.
        var errors = SettingsValidator.Validate(settings with { Rooms = rooms });
        foreach (var error in errors)
        {
            logger.LogWarning(
                "Seeded configuration is questionable at {Field}: {Message}",
                error.Field, error.Message);
        }

        await store.SaveAsync(settings, ct);

        if (rooms.Count > 0)
        {
            // Skip rooms that would violate a unique index, rather than failing
            // the whole seed and leaving the kiosk with none at all.
            var usable = Deduplicate(rooms, logger);
            await store.SaveRoomsAsync(usable, ct);
            logger.LogInformation("Seeded {Count} rooms from {Path}", usable.Count, roomsFilePath);
        }
        else
        {
            logger.LogWarning(
                "No rooms found at {Path}. The kiosk will start with an empty directory; "
                + "add rooms from Instellingen → Zalen.",
                roomsFilePath);
        }

        var secret = configuration["Graph:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(secret))
        {
            await store.SetGraphSecretAsync(secret, ct);
            logger.LogInformation(
                "Imported the Graph client secret from configuration and encrypted it. "
                + "Remove it from config/appsettings.Production.json now; it is no longer read.");
        }

        await store.MarkSeededAsync(ct);
    }

    /// <summary>
    /// Pulls the old file-based settings across, falling back to each record's
    /// own default where a key was never set.
    ///
    /// Written out key by key rather than bound reflectively so that it is
    /// obvious exactly which settings migrate, and so a renamed property cannot
    /// silently stop carrying a value forward.
    /// </summary>
    private static KioskSettings ReadFromConfiguration(IConfiguration configuration)
    {
        var graph = new GraphSettings();
        var sync = new SyncSettings();
        var device = new DeviceSettings();
        var privacy = new PrivacySettings();
        var ui = new UiSettings();

        return new KioskSettings
        {
            Graph = new GraphSettings
            {
                TenantId = configuration["Graph:TenantId"] ?? graph.TenantId,
                ClientId = configuration["Graph:ClientId"] ?? graph.ClientId,
                BaseUrl = configuration["Graph:BaseUrl"] ?? graph.BaseUrl,
                TimeZone = configuration["Graph:TimeZone"] ?? graph.TimeZone,
            },
            Sync = new SyncSettings
            {
                Interval = configuration.GetValue("Sync:Interval", sync.Interval),
                WindowBehind = configuration.GetValue("Sync:WindowBehind", sync.WindowBehind),
                WindowAhead = configuration.GetValue("Sync:WindowAhead", sync.WindowAhead),
                StaleAfter = configuration.GetValue("Sync:StaleAfter", sync.StaleAfter),
                // Retention used to live under Storage, which is where an
                // upgrading install still has it.
                RetainPastMeetingsFor = configuration.GetValue(
                    "Storage:RetainPastMeetingsFor", sync.RetainPastMeetingsFor),
            },
            Device = new DeviceSettings
            {
                BusId = configuration.GetValue("Device:BusId", device.BusId),
                Address = configuration.GetValue("Device:Address", device.Address),
                AttentionPin = configuration.GetValue("Device:AttentionPin", device.AttentionPin),
                ResponseDelay = configuration.GetValue("Device:ResponseDelay", device.ResponseDelay),
                MaxRetries = configuration.GetValue("Device:MaxRetries", device.MaxRetries),
                MaxCommandsPerSecond = configuration.GetValue(
                    "Device:MaxCommandsPerSecond", device.MaxCommandsPerSecond),
            },
            // Nothing in the old config matches these, and the defaults are the
            // conservative choice: subjects hidden.
            Privacy = privacy,
            Ui = ui,
        };
    }

    private static IReadOnlyList<Room> Deduplicate(
        IReadOnlyList<Room> rooms, ILogger logger)
    {
        var kept = new List<Room>(rooms.Count);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mailboxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signals = new HashSet<byte>();

        foreach (var room in rooms)
        {
            if (!ids.Add(room.Id) || !mailboxes.Add(room.Mailbox) || !signals.Add(room.SignalId))
            {
                logger.LogWarning(
                    "Skipping room {RoomId} while seeding: its id, mailbox or indicator "
                    + "duplicates an earlier room",
                    room.Id);
                continue;
            }

            kept.Add(room);
        }

        return kept;
    }
}
