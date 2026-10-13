using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Zuil.Core.Abstractions;
using Zuil.Core.Admin;
using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Storage;

/// <summary>
/// Settings and rooms in SQLite, beside the meeting cache.
///
/// Implements two interfaces over one table on purpose: <see cref="ISettingsStore"/>
/// is what the admin panel gets and has no way to read the Graph secret, while
/// <see cref="IGraphCredentialSource"/> is what the token provider gets and is
/// the only path to it. Registering the same instance under both keeps that split
/// honest without a second connection.
/// </summary>
public sealed class SqliteSettingsStore : ISettingsStore, IGraphCredentialSource
{
    // Configuration-style colon notation, so a key reads the same here as it
    // does in appsettings.json.
    private const string KeyTenantId = "Graph:TenantId";
    private const string KeyClientId = "Graph:ClientId";
    private const string KeyBaseUrl = "Graph:BaseUrl";
    private const string KeyTimeZone = "Graph:TimeZone";
    private const string KeyClientSecret = "Graph:ClientSecret";
    private const string KeyCertificatePath = "Graph:CertificatePath";
    private const string KeyCertificatePassword = "Graph:CertificatePassword";

    private const string KeySyncInterval = "Sync:Interval";
    private const string KeySyncWindowBehind = "Sync:WindowBehind";
    private const string KeySyncWindowAhead = "Sync:WindowAhead";
    private const string KeySyncStaleAfter = "Sync:StaleAfter";
    private const string KeySyncRetainFor = "Sync:RetainPastMeetingsFor";

    private const string KeyDeviceBusId = "Device:BusId";
    private const string KeyDeviceAddress = "Device:Address";
    private const string KeyDeviceAttentionPin = "Device:AttentionPin";
    private const string KeyDeviceResponseDelay = "Device:ResponseDelay";
    private const string KeyDeviceMaxRetries = "Device:MaxRetries";
    private const string KeyDeviceMaxPerSecond = "Device:MaxCommandsPerSecond";

    private const string KeySubjectVisibility = "Privacy:SubjectVisibility";
    private const string KeyIdleTimeout = "Privacy:IdleTimeout";

    private const string KeyAttractView = "Ui:AttractView";
    private const string KeyUpcomingCount = "Ui:UpcomingCount";
    private const string KeyShowAgendaNav = "Ui:ShowAgendaNav";

    private const string KeySessionIdleTimeout = "Security:SessionIdleTimeout";
    private const string KeyMaxFailedAttempts = "Security:MaxFailedAttempts";
    private const string KeyLockoutWindow = "Security:LockoutWindow";

    private const string KeyPort = "Network:Port";

    private const string KeyLaunchBrowser = "Browser:LaunchBrowser";
    private const string KeyBrowserPath = "Browser:BrowserPath";

    private const string KeySeeded = "Schema:Seeded";

    private readonly ZuilDatabase _db;
    private readonly AesGcmSecretProtector _protector;
    private readonly ILogger<SqliteSettingsStore> _logger;

    public SqliteSettingsStore(
        ZuilDatabase db,
        AesGcmSecretProtector protector,
        ILogger<SqliteSettingsStore> logger)
    {
        _db = db;
        _protector = protector;
        _logger = logger;
    }

    public async Task<KioskSettings> LoadAsync(CancellationToken ct)
    {
        var values = await ReadAllAsync(ct);
        var rooms = await ReadRoomsAsync(ct);

        // Every getter falls back to the record's own default, so a fresh install
        // with an empty table loads cleanly and boots.
        var graphDefaults = new GraphSettings();
        var syncDefaults = new SyncSettings();
        var deviceDefaults = new DeviceSettings();
        var privacyDefaults = new PrivacySettings();
        var uiDefaults = new UiSettings();
        var securityDefaults = new SecuritySettings();
        var networkDefaults = new NetworkSettings();
        var browserDefaults = new BrowserSettings();

        return new KioskSettings
        {
            Graph = new GraphSettings
            {
                TenantId = Text(values, KeyTenantId, graphDefaults.TenantId),
                ClientId = Text(values, KeyClientId, graphDefaults.ClientId),
                BaseUrl = Text(values, KeyBaseUrl, graphDefaults.BaseUrl),
                TimeZone = Text(values, KeyTimeZone, graphDefaults.TimeZone),
                CertificatePath = Text(values, KeyCertificatePath, graphDefaults.CertificatePath),
                CertificatePasswordIsSet =
                    !string.IsNullOrWhiteSpace(Text(values, KeyCertificatePassword, "")),
                SecretIsSet = !string.IsNullOrWhiteSpace(Text(values, KeyClientSecret, "")),
            },
            Sync = new SyncSettings
            {
                Interval = Duration(values, KeySyncInterval, syncDefaults.Interval),
                WindowBehind = Duration(values, KeySyncWindowBehind, syncDefaults.WindowBehind),
                WindowAhead = Duration(values, KeySyncWindowAhead, syncDefaults.WindowAhead),
                StaleAfter = Duration(values, KeySyncStaleAfter, syncDefaults.StaleAfter),
                RetainPastMeetingsFor =
                    Duration(values, KeySyncRetainFor, syncDefaults.RetainPastMeetingsFor),
            },
            Device = new DeviceSettings
            {
                BusId = Number(values, KeyDeviceBusId, deviceDefaults.BusId),
                Address = Number(values, KeyDeviceAddress, deviceDefaults.Address),
                AttentionPin = NullableNumber(values, KeyDeviceAttentionPin, deviceDefaults.AttentionPin),
                ResponseDelay = Duration(values, KeyDeviceResponseDelay, deviceDefaults.ResponseDelay),
                MaxRetries = Number(values, KeyDeviceMaxRetries, deviceDefaults.MaxRetries),
                MaxCommandsPerSecond =
                    Number(values, KeyDeviceMaxPerSecond, deviceDefaults.MaxCommandsPerSecond),
            },
            Privacy = new PrivacySettings
            {
                SubjectVisibility =
                    Choice(values, KeySubjectVisibility, privacyDefaults.SubjectVisibility),
                IdleTimeout = Duration(values, KeyIdleTimeout, privacyDefaults.IdleTimeout),
            },
            Ui = new UiSettings
            {
                AttractView = Choice(values, KeyAttractView, uiDefaults.AttractView),
                UpcomingCount = Number(values, KeyUpcomingCount, uiDefaults.UpcomingCount),
                ShowAgendaNav = Flag(values, KeyShowAgendaNav, uiDefaults.ShowAgendaNav),
            },
            Security = new SecuritySettings
            {
                SessionIdleTimeout = Duration(
                    values, KeySessionIdleTimeout, securityDefaults.SessionIdleTimeout),
                MaxFailedAttempts = Number(
                    values, KeyMaxFailedAttempts, securityDefaults.MaxFailedAttempts),
                LockoutWindow = Duration(values, KeyLockoutWindow, securityDefaults.LockoutWindow),
            },
            Network = new NetworkSettings
            {
                Port = Number(values, KeyPort, networkDefaults.Port),
            },
            Browser = new BrowserSettings
            {
                LaunchBrowser = Flag(values, KeyLaunchBrowser, browserDefaults.LaunchBrowser),
                BrowserPath = Text(values, KeyBrowserPath, browserDefaults.BrowserPath),
            },
            Rooms = rooms,
        };
    }

    public async Task SaveAsync(KioskSettings settings, CancellationToken ct)
    {
        // Note what is absent: Graph:ClientSecret. It is written only by
        // SetGraphSecretAsync, so a round-trip of the settings object can never
        // overwrite the credential with a blank.
        var writes = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [KeyTenantId] = settings.Graph.TenantId,
            [KeyClientId] = settings.Graph.ClientId,
            [KeyBaseUrl] = settings.Graph.BaseUrl,
            [KeyTimeZone] = settings.Graph.TimeZone,
            [KeyCertificatePath] = settings.Graph.CertificatePath,

            [KeySyncInterval] = Iso(settings.Sync.Interval),
            [KeySyncWindowBehind] = Iso(settings.Sync.WindowBehind),
            [KeySyncWindowAhead] = Iso(settings.Sync.WindowAhead),
            [KeySyncStaleAfter] = Iso(settings.Sync.StaleAfter),
            [KeySyncRetainFor] = Iso(settings.Sync.RetainPastMeetingsFor),

            [KeyDeviceBusId] = Int(settings.Device.BusId),
            [KeyDeviceAddress] = Int(settings.Device.Address),
            [KeyDeviceAttentionPin] = settings.Device.AttentionPin is { } pin ? Int(pin) : null,
            [KeyDeviceResponseDelay] = Iso(settings.Device.ResponseDelay),
            [KeyDeviceMaxRetries] = Int(settings.Device.MaxRetries),
            [KeyDeviceMaxPerSecond] = Int(settings.Device.MaxCommandsPerSecond),

            [KeySubjectVisibility] = settings.Privacy.SubjectVisibility.ToString(),
            [KeyIdleTimeout] = Iso(settings.Privacy.IdleTimeout),

            [KeyAttractView] = settings.Ui.AttractView.ToString(),
            [KeyUpcomingCount] = Int(settings.Ui.UpcomingCount),
            [KeyShowAgendaNav] = settings.Ui.ShowAgendaNav ? "true" : "false",

            [KeySessionIdleTimeout] = Iso(settings.Security.SessionIdleTimeout),
            [KeyMaxFailedAttempts] = Int(settings.Security.MaxFailedAttempts),
            [KeyLockoutWindow] = Iso(settings.Security.LockoutWindow),

            [KeyPort] = Int(settings.Network.Port),

            [KeyLaunchBrowser] = settings.Browser.LaunchBrowser ? "true" : "false",
            [KeyBrowserPath] = settings.Browser.BrowserPath,
        };

        using var lease = await _db.AcquireWriteAsync(ct);
        await using var tx = (SqliteTransaction)await _db.Connection.BeginTransactionAsync(ct);

        foreach (var (key, value) in writes)
        {
            await UpsertAsync(tx, key, value, isSecret: false, ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task SaveRoomsAsync(IReadOnlyList<Room> rooms, CancellationToken ct)
    {
        using var lease = await _db.AcquireWriteAsync(ct);
        await using var tx = (SqliteTransaction)await _db.Connection.BeginTransactionAsync(ct);

        // Replace rather than merge: the caller has already validated the set as
        // a whole, and a delete-then-insert inside one transaction cannot trip
        // the unique indexes on an id that merely moved position.
        await using (var clear = _db.Connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM room;";
            await clear.ExecuteNonQueryAsync(ct);
        }

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];

            await using var insert = _db.Connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO room
                    (id, mailbox, display_name, floor, directions, signal_id, sort_order,
                     is_enabled, updated_utc)
                VALUES
                    ($id, $mailbox, $name, $floor, $directions, $signal, $order, 1, $now);
                """;
            insert.Parameters.AddWithValue("$id", room.Id);
            insert.Parameters.AddWithValue("$mailbox", room.Mailbox);
            insert.Parameters.AddWithValue("$name", room.DisplayName);
            insert.Parameters.AddWithValue("$floor", room.Floor);
            insert.Parameters.AddWithValue("$directions", room.Directions);
            insert.Parameters.AddWithValue("$signal", (int)room.SignalId);
            insert.Parameters.AddWithValue("$order", i);
            insert.Parameters.AddWithValue("$now", Timestamp());
            await insert.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        _logger.LogInformation("Room directory replaced with {Count} rooms", rooms.Count);
    }

    public async Task SetGraphSecretAsync(string? secret, CancellationToken ct)
    {
        var stored = string.IsNullOrWhiteSpace(secret) ? null : _protector.Protect(secret);

        using var lease = await _db.AcquireWriteAsync(ct);
        await UpsertAsync(transaction: null, KeyClientSecret, stored, isSecret: true, ct);

        // Deliberately no value, and no length: a length leaks something.
        _logger.LogInformation(
            "Graph client secret {Action}", stored is null ? "cleared" : "updated");
    }

    public async Task SetCertificatePasswordAsync(string? password, CancellationToken ct)
    {
        var stored = string.IsNullOrWhiteSpace(password) ? null : _protector.Protect(password);

        using var lease = await _db.AcquireWriteAsync(ct);
        await UpsertAsync(transaction: null, KeyCertificatePassword, stored, isSecret: true, ct);

        _logger.LogInformation(
            "Graph certificate password {Action}", stored is null ? "cleared" : "updated");
    }

    public async Task<bool> HasGraphSecretAsync(CancellationToken ct)
    {
        var stored = await ReadValueAsync(KeyClientSecret, ct);
        return !string.IsNullOrWhiteSpace(stored);
    }

    public async Task<GraphCredentials?> GetAsync(CancellationToken ct)
    {
        var values = await ReadAllAsync(ct);

        var tenantId = Text(values, KeyTenantId, "");
        var clientId = Text(values, KeyClientId, "");

        string? secret = null;
        var stored = Text(values, KeyClientSecret, "");
        if (!string.IsNullOrWhiteSpace(stored))
        {
            // Null on a key mismatch, which reads downstream as "not configured"
            // rather than crashing a wall-mounted kiosk on boot.
            secret = _protector.Unprotect(stored);
        }

        var certificatePath = Text(values, KeyCertificatePath, "");

        string? certificatePassword = null;
        var storedCertificatePassword = Text(values, KeyCertificatePassword, "");
        if (!string.IsNullOrWhiteSpace(storedCertificatePassword))
        {
            certificatePassword = _protector.Unprotect(storedCertificatePassword);
        }

        var credentials = new GraphCredentials(
            TenantId: tenantId,
            ClientId: clientId,
            ClientSecret: secret,
            CertificatePath: string.IsNullOrWhiteSpace(certificatePath) ? null : certificatePath,
            CertificatePassword: certificatePassword);

        return credentials.IsComplete ? credentials : null;
    }

    public async Task<bool> IsSeededAsync(CancellationToken ct) =>
        await ReadValueAsync(KeySeeded, ct) == "1";

    public async Task MarkSeededAsync(CancellationToken ct)
    {
        using var lease = await _db.AcquireWriteAsync(ct);
        await UpsertAsync(transaction: null, KeySeeded, "1", isSecret: false, ct);
    }

    public async Task<AdminCredential?> GetAdminCredentialAsync(CancellationToken ct)
    {
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT username, password_hash, is_default FROM admin_credential WHERE id = 1;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new AdminCredential(
            Username: reader.GetString(0),
            PasswordHash: reader.GetString(1),
            IsDefault: reader.GetInt32(2) != 0);
    }

    public async Task SetAdminCredentialAsync(AdminCredential credential, CancellationToken ct)
    {
        using var lease = await _db.AcquireWriteAsync(ct);

        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO admin_credential (id, username, password_hash, is_default, updated_utc)
            VALUES (1, $username, $hash, $default, $now)
            ON CONFLICT (id) DO UPDATE
               SET username      = excluded.username,
                   password_hash = excluded.password_hash,
                   is_default    = excluded.is_default,
                   updated_utc   = excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$username", credential.Username);
        cmd.Parameters.AddWithValue("$hash", credential.PasswordHash);
        cmd.Parameters.AddWithValue("$default", credential.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", Timestamp());
        await cmd.ExecuteNonQueryAsync(ct);

        // Deliberately no value, and no indication of length.
        _logger.LogInformation("Admin credentials updated");
    }

    public async Task RecordLoginAttemptAsync(DateTimeOffset at, bool succeeded, CancellationToken ct)
    {
        using var lease = await _db.AcquireWriteAsync(ct);

        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO admin_login_attempt (at_utc, succeeded) VALUES ($at, $ok);
            """;
        cmd.Parameters.AddWithValue("$at", Iso(at));
        cmd.Parameters.AddWithValue("$ok", succeeded ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CountFailedLoginsAsync(DateTimeOffset since, CancellationToken ct)
    {
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM admin_login_attempt
             WHERE succeeded = 0 AND at_utc >= $since;
            """;
        cmd.Parameters.AddWithValue("$since", Iso(since));

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long count ? (int)count : 0;
    }

    public async Task ClearLoginAttemptsAsync(CancellationToken ct)
    {
        using var lease = await _db.AcquireWriteAsync(ct);

        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM admin_login_attempt;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<Dictionary<string, string?>> ReadAllAsync(CancellationToken ct)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM setting;";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return values;
    }

    private async Task<string?> ReadValueAsync(string key, CancellationToken ct)
    {
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM setting WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string text ? text : null;
    }

    private async Task<IReadOnlyList<Room>> ReadRoomsAsync(CancellationToken ct)
    {
        var rooms = new List<Room>();

        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, mailbox, display_name, floor, directions, signal_id
              FROM room
             WHERE is_enabled = 1
             ORDER BY sort_order, display_name;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var signal = reader.GetInt32(5);

            rooms.Add(new Room
            {
                Id = reader.GetString(0),
                Mailbox = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Floor = reader.GetString(3),
                Directions = reader.GetString(4),
                SignalId = signal is >= 0 and <= byte.MaxValue ? (byte)signal : (byte)0,
            });
        }

        return rooms;
    }

    /// <summary>
    /// Writes one key. Pass <paramref name="transaction"/> when batching; the
    /// caller holds the write lease either way.
    /// </summary>
    private async Task UpsertAsync(
        SqliteTransaction? transaction,
        string key,
        string? value,
        bool isSecret,
        CancellationToken ct)
    {
        await using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO setting (key, value, is_secret, updated_utc)
            VALUES ($key, $value, $secret, $now)
            ON CONFLICT (key) DO UPDATE
               SET value       = excluded.value,
                   is_secret   = excluded.is_secret,
                   updated_utc = excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$secret", isSecret ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", Timestamp());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Text(IReadOnlyDictionary<string, string?> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static TimeSpan Duration(
        IReadOnlyDictionary<string, string?> values, string key, TimeSpan fallback) =>
        values.TryGetValue(key, out var value)
        && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static int Number(IReadOnlyDictionary<string, string?> values, string key, int fallback) =>
        values.TryGetValue(key, out var value)
        && int.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static int? NullableNumber(
        IReadOnlyDictionary<string, string?> values, string key, int? fallback)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }

        // An explicitly stored null means "no attention pin", which is different
        // from the key being absent.
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static bool Flag(IReadOnlyDictionary<string, string?> values, string key, bool fallback) =>
        values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : fallback;

    private static TEnum Choice<TEnum>(
        IReadOnlyDictionary<string, string?> values, string key, TEnum fallback)
        where TEnum : struct, Enum =>
        values.TryGetValue(key, out var value)
        && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static string Iso(TimeSpan value) =>
        value.ToString("c", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static string Int(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Timestamp() => Iso(DateTimeOffset.UtcNow);
}
