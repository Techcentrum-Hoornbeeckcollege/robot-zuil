using System.Globalization;
using Zuil.Core.Models;

namespace Zuil.Core.Settings;

/// <summary>One rejected field, named so the admin panel can mark it in place.</summary>
/// <param name="Field">Dotted path, e.g. "sync.interval" or "rooms[2].signalId".</param>
/// <param name="Message">Dutch, shown to whoever is standing at the kiosk.</param>
public sealed record FieldError(string Field, string Message);

/// <summary>
/// Validates what an administrator typed, before it reaches the database.
///
/// Pure and in Core on purpose. The same rules run on the admin PUT and on the
/// first-run seed, so a constraint like "two rooms may not share an indicator"
/// cannot be true in one path and not the other — and it is enforced on write,
/// as an HTTP 400, rather than by throwing at startup and bricking a kiosk whose
/// only repair tool is the GUI it just refused to boot.
/// </summary>
public static class SettingsValidator
{
    // A secret pasted back from the UI's own mask would silently overwrite the
    // real credential with bullet characters.
    private const char MaskCharacter = '•';

    public static IReadOnlyList<FieldError> Validate(KioskSettings settings)
    {
        var errors = new List<FieldError>();

        ValidateGraph(settings.Graph, errors);
        ValidateSync(settings.Sync, errors);
        ValidateDevice(settings.Device, errors);
        ValidatePrivacy(settings.Privacy, errors);
        ValidateUi(settings.Ui, errors);
        errors.AddRange(ValidateRooms(settings.Rooms));

        return errors;
    }

    /// <summary>
    /// Validates the room set as a whole. Uniqueness is a set-level property, so
    /// it cannot be checked one room at a time — which is why the admin API
    /// replaces the whole array rather than offering a per-room PATCH.
    /// </summary>
    public static IReadOnlyList<FieldError> ValidateRooms(IReadOnlyList<Room> rooms)
    {
        var errors = new List<FieldError>();

        var seenIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenMailboxes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenSignals = new Dictionary<byte, int>();

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];

            Required(errors, $"rooms[{i}].id", room.Id, "Een zaal heeft een id nodig.");
            Required(errors, $"rooms[{i}].displayName", room.DisplayName, "Vul een naam in.");
            Required(errors, $"rooms[{i}].floor", room.Floor, "Vul een verdieping in.");
            Required(errors, $"rooms[{i}].directions", room.Directions, "Vul een looproute in.");

            if (string.IsNullOrWhiteSpace(room.Mailbox))
            {
                errors.Add(new FieldError($"rooms[{i}].mailbox", "Vul het postvak van de zaal in."));
            }
            else if (!LooksLikeMailbox(room.Mailbox))
            {
                errors.Add(new FieldError(
                    $"rooms[{i}].mailbox", "Dit lijkt geen e-mailadres."));
            }

            // Zero is the protocol's "no indicator", so a room carrying it would
            // light nothing and give no hint as to why.
            if (room.SignalId == 0)
            {
                errors.Add(new FieldError(
                    $"rooms[{i}].signalId", "Kies een indicator (1 of hoger)."));
            }

            AddIfDuplicate(errors, seenIds, room.Id, i, $"rooms[{i}].id",
                "Deze id is al in gebruik.");
            AddIfDuplicate(errors, seenMailboxes, room.Mailbox, i, $"rooms[{i}].mailbox",
                "Dit postvak is al aan een andere zaal gekoppeld.");

            if (room.SignalId != 0)
            {
                if (seenSignals.TryGetValue(room.SignalId, out var first))
                {
                    // Two rooms sharing an indicator means the column points at
                    // the wrong place half the time.
                    errors.Add(new FieldError(
                        $"rooms[{i}].signalId",
                        $"Indicator {room.SignalId} is al gekozen voor zaal {first + 1}."));
                }
                else
                {
                    seenSignals[room.SignalId] = i;
                }
            }
        }

        return errors;
    }

    private static void ValidateGraph(GraphSettings graph, List<FieldError> errors)
    {
        // Blank is allowed: a fresh install must be able to boot unconfigured and
        // be filled in from the admin panel. Malformed is not.
        if (!string.IsNullOrWhiteSpace(graph.TenantId) && !Guid.TryParse(graph.TenantId, out _))
        {
            errors.Add(new FieldError("graph.tenantId", "Dit is geen geldige tenant-id (GUID)."));
        }

        if (!string.IsNullOrWhiteSpace(graph.ClientId) && !Guid.TryParse(graph.ClientId, out _))
        {
            errors.Add(new FieldError("graph.clientId", "Dit is geen geldige client-id (GUID)."));
        }

        if (string.IsNullOrWhiteSpace(graph.TimeZone))
        {
            errors.Add(new FieldError("graph.timeZone", "Vul een tijdzone in."));
        }

        if (!Uri.TryCreate(graph.BaseUrl, UriKind.Absolute, out var baseUrl)
            || baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add(new FieldError("graph.baseUrl", "De Graph-URL moet een https-adres zijn."));
        }
    }

    private static void ValidateSync(SyncSettings sync, List<FieldError> errors)
    {
        Range(errors, "sync.interval", sync.Interval,
            TimeSpan.FromMinutes(1), TimeSpan.FromHours(1),
            "Kies tussen 1 en 60 minuten.");

        Range(errors, "sync.windowBehind", sync.WindowBehind,
            TimeSpan.Zero, TimeSpan.FromHours(24),
            "Kies tussen 0 en 24 uur.");

        Range(errors, "sync.windowAhead", sync.WindowAhead,
            TimeSpan.FromHours(1), TimeSpan.FromDays(7),
            "Kies tussen 1 uur en 7 dagen.");

        Range(errors, "sync.staleAfter", sync.StaleAfter,
            TimeSpan.FromMinutes(1), TimeSpan.FromHours(24),
            "Kies tussen 1 minuut en 24 uur.");

        Range(errors, "sync.retainPastMeetingsFor", sync.RetainPastMeetingsFor,
            TimeSpan.Zero, TimeSpan.FromDays(7),
            "Kies tussen 0 en 7 dagen.");

        // A staleness warning that fires before the next sync can possibly have
        // run would leave the banner up permanently.
        if (sync.StaleAfter <= sync.Interval)
        {
            errors.Add(new FieldError(
                "sync.staleAfter",
                "Moet ruimer zijn dan het synchronisatie-interval, anders staat de "
                + "waarschuwing altijd aan."));
        }
    }

    private static void ValidateDevice(DeviceSettings device, List<FieldError> errors)
    {
        if (device.BusId is < 0 or > 10)
        {
            errors.Add(new FieldError("device.busId", "Kies tussen 0 en 10."));
        }

        // 0x08..0x77 is the usable 7-bit i2c range; the rest is reserved.
        if (device.Address is < 0x08 or > 0x77)
        {
            errors.Add(new FieldError("device.address", "Kies een i2c-adres tussen 0x08 en 0x77."));
        }

        if (device.AttentionPin is { } pin && pin is < 0 or > 27)
        {
            errors.Add(new FieldError("device.attentionPin", "Kies een BCM-pin tussen 0 en 27."));
        }

        Range(errors, "device.responseDelay", device.ResponseDelay,
            TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1),
            "Kies tussen 1 en 1000 ms.");

        if (device.MaxRetries is < 0 or > 10)
        {
            errors.Add(new FieldError("device.maxRetries", "Kies tussen 0 en 10."));
        }

        if (device.MaxCommandsPerSecond is < 1 or > 100)
        {
            errors.Add(new FieldError("device.maxCommandsPerSecond", "Kies tussen 1 en 100."));
        }
    }

    private static void ValidatePrivacy(PrivacySettings privacy, List<FieldError> errors)
    {
        if (!Enum.IsDefined(privacy.SubjectVisibility))
        {
            errors.Add(new FieldError("privacy.subjectVisibility", "Onbekende keuze."));
        }

        Range(errors, "privacy.idleTimeout", privacy.IdleTimeout,
            TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10),
            "Kies tussen 10 seconden en 10 minuten.");
    }

    private static void ValidateUi(UiSettings ui, List<FieldError> errors)
    {
        if (!Enum.IsDefined(ui.AttractView))
        {
            errors.Add(new FieldError("ui.attractView", "Onbekende keuze."));
        }

        if (ui.UpcomingCount is < 1 or > 100)
        {
            errors.Add(new FieldError("ui.upcomingCount", "Kies tussen 1 en 100."));
        }
    }

    /// <summary>
    /// Rejects a submitted client secret that is only mask characters, which is
    /// what happens when someone copies the UI's own placeholder back into the
    /// field. Returns null when the value is acceptable.
    /// </summary>
    public static FieldError? ValidateSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return new FieldError("graph.secret", "Vul de client secret in.");
        }

        if (secret.All(c => c == MaskCharacter))
        {
            return new FieldError(
                "graph.secret",
                "Dit is de maskering, niet de secret. Plak de echte waarde uit Entra.");
        }

        return null;
    }

    private static void Required(
        List<FieldError> errors, string field, string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new FieldError(field, message));
        }
    }

    private static void Range(
        List<FieldError> errors,
        string field,
        TimeSpan value,
        TimeSpan min,
        TimeSpan max,
        string message)
    {
        if (value < min || value > max)
        {
            errors.Add(new FieldError(field, message));
        }
    }

    private static void AddIfDuplicate(
        List<FieldError> errors,
        Dictionary<string, int> seen,
        string value,
        int index,
        string field,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (seen.TryGetValue(value, out var first))
        {
            errors.Add(new FieldError(
                field,
                string.Format(CultureInfo.InvariantCulture, "{0} (zie zaal {1})", message, first + 1)));
            return;
        }

        seen[value] = index;
    }

    private static bool LooksLikeMailbox(string value)
    {
        // Deliberately shallow. The authoritative check is whether Graph answers
        // for the mailbox, which the admin panel's "test" button exercises.
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0
            && at == value.LastIndexOf('@')
            && at < value.Length - 1
            && !value.Any(char.IsWhiteSpace)
            && value.IndexOf('.', at) > at + 1;
    }
}
