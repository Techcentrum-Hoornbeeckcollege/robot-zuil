using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using Zuil.Core.Abstractions;
using Zuil.Core.Admin;
using Zuil.Core.Models;
using Zuil.Core.Settings;
using Zuil.Graph;
using Zuil.Host.Admin;
using Zuil.Host.Hubs;
using Zuil.Host.Workers;

namespace Zuil.Host.Endpoints;

/// <summary>
/// Everything behind Instellingen → Beheer.
///
/// Two groups rather than one: the session routes have to be reachable without a
/// session, and everything else is behind <see cref="AdminSessionFilter"/>. The
/// split is visible in the route table instead of hidden in middleware ordering.
///
/// Durations cross this API as plain numbers in whatever unit the form field
/// uses — minutes, hours, milliseconds — rather than as TimeSpan strings. It
/// keeps the touchscreen inputs simple and means the client never has to build
/// an "hh:mm:ss" by hand.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Named rate-limit policy, applied to the login route only.</summary>
    public const string LoginRateLimitPolicy = "admin-login";

    /// <summary>
    /// One log category for the admin surface, so a deployment can raise or
    /// silence it without touching the rest of the host.
    /// </summary>
    private const string AdminLogCategory = "Zuil.Host.Admin";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        MapSessionEndpoints(app);

        var admin = app.MapGroup("/api/admin").AddEndpointFilter<AdminSessionFilter>();

        MapSettingsEndpoints(admin);
        MapAdminRoomEndpoints(admin);
        MapToolEndpoints(admin);
    }

    private static void MapSessionEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/session", (
            HttpContext context,
            AdminSessionStore sessions,
            IOptions<AdminOptions> options) =>
        {
            var token = AdminSession.TokenFrom(context.Request);
            var authenticated = token is not null && sessions.Validate(AdminSession.Hash(token));

            return Results.Ok(new AdminSessionResponse(
                Authenticated: authenticated,
                PasswordIsDefault: options.Value.IsDefault));
        });

        app.MapPost("/api/admin/session", async (
            AdminLoginRequest request,
            HttpContext context,
            AdminSessionStore sessions,
            ISettingsStore store,
            IOptions<AdminOptions> options,
            IClock clock,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(AdminLogCategory);
            var admin = options.Value;
            var now = clock.Now;

            // The persistent ledger is what makes the lockout survive someone
            // pulling the plug, which an in-memory counter does not.
            var failures = await store.CountFailedLoginsAsync(now - admin.LockoutWindow, ct);
            if (failures >= admin.MaxFailedAttempts)
            {
                logger.LogWarning(
                    "Admin login refused: locked out after {Failures} failures from {Ip}",
                    failures, context.Connection.RemoteIpAddress);

                return Results.Json(
                    new AdminErrorResponse(
                        "Te veel mislukte pogingen. Probeer het over een paar minuten opnieuw."),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            if (!Matches(request.Password, admin.Password))
            {
                await store.RecordLoginAttemptAsync(now, succeeded: false, ct);

                // Never the attempted password, in a log an administrator may
                // well paste into a support ticket.
                logger.LogWarning(
                    "Admin login failed from {Ip}", context.Connection.RemoteIpAddress);

                // A fixed cost on failure, so the endpoint is not a fast oracle.
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);

                return Results.Json(
                    new AdminErrorResponse("Onjuist wachtwoord."),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            await store.RecordLoginAttemptAsync(now, succeeded: true, ct);
            await store.ClearLoginAttemptsAsync(ct);

            var token = AdminSession.NewToken();
            sessions.Create(AdminSession.Hash(token));
            context.Response.Cookies.Append(
                AdminSession.CookieName, token, AdminSession.CookieOptions());

            logger.LogInformation("Admin signed in");

            return Results.Ok(new AdminSessionResponse(
                Authenticated: true,
                PasswordIsDefault: admin.IsDefault));
        })
        .RequireRateLimiting(LoginRateLimitPolicy);

        app.MapDelete("/api/admin/session", (HttpContext context, AdminSessionStore sessions) =>
        {
            if (AdminSession.TokenFrom(context.Request) is { } token)
            {
                sessions.Remove(AdminSession.Hash(token));
            }

            context.Response.Cookies.Append(
                AdminSession.CookieName, "", AdminSession.CookieOptions(deleting: true));

            return Results.NoContent();
        });
    }

    private static void MapSettingsEndpoints(IEndpointRouteBuilder admin)
    {
        admin.MapGet("/settings", async (
            ISettingsProvider settings,
            ISettingsStore store,
            IOptions<AdminOptions> options,
            CancellationToken ct) =>
        {
            var current = settings.Current;
            var secretIsSet = await store.HasGraphSecretAsync(ct);

            return Results.Ok(ToResponse(current, secretIsSet, options.Value));
        });

        admin.MapPut("/settings", async (
            AdminSettingsRequest request,
            ISettingsProvider settings,
            ISettingsStore store,
            GraphTokenProvider tokens,
            CalendarSyncWorker worker,
            IHubContext<StatusHub> hub,
            IOptions<AdminOptions> options,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(AdminLogCategory);
            var current = settings.Current;
            var updated = Apply(current, request);

            // Rooms are not part of this payload, but the validator wants the
            // whole object; carry the current ones through so their errors do
            // not surface here.
            var errors = SettingsValidator.Validate(updated with { Rooms = current.Rooms });

            // The secret is validated separately, since it is not part of the
            // settings record at all.
            FieldError? secretError = null;
            var action = request.Graph?.SecretAction ?? SecretAction.Keep;
            if (action == SecretAction.Set)
            {
                secretError = SettingsValidator.ValidateSecret(request.Graph?.Secret ?? "");
            }

            if (errors.Count > 0 || secretError is not null)
            {
                var all = new List<FieldError>(errors);
                if (secretError is not null)
                {
                    all.Add(secretError);
                }

                return Results.BadRequest(new AdminValidationResponse(
                    [.. all.Select(e => new AdminFieldError(e.Field, e.Message))]));
            }

            await store.SaveAsync(updated, ct);

            switch (action)
            {
                case SecretAction.Set:
                    await store.SetGraphSecretAsync(request.Graph!.Secret, ct);
                    break;
                case SecretAction.Clear:
                    await store.SetGraphSecretAsync(null, ct);
                    break;
                case SecretAction.Keep:
                default:
                    break;
            }

            // Drop the MSAL client before anything can ask for a token, so a
            // "test connection" straight after saving cannot pass on a token
            // minted with the previous secret.
            tokens.Invalidate();

            await settings.ReloadAsync(ct);

            // Tell every open kiosk page, so a new idle timeout, privacy setting
            // or room list lands without a reload. This is what makes it one
            // page rather than a page you have to restart.
            await hub.Clients.All.SendAsync("settingsUpdated", ct);

            // Settings changed; resync now rather than at the next interval.
            worker.Wake();

            logger.LogInformation("Settings updated from the admin panel");

            var secretIsSet = await store.HasGraphSecretAsync(ct);
            return Results.Ok(ToResponse(settings.Current, secretIsSet, options.Value));
        });
    }

    private static void MapAdminRoomEndpoints(IEndpointRouteBuilder admin)
    {
        admin.MapPut("/rooms", async (
            AdminRoomsRequest request,
            ISettingsProvider settings,
            ISettingsStore store,
            IHubContext<StatusHub> hub,
            CalendarSyncWorker worker,
            CancellationToken ct) =>
        {
            var rooms = request.Rooms
                .Select(r => new Room
                {
                    Id = (string.IsNullOrWhiteSpace(r.Id) ? Slug(r.DisplayName) : r.Id).Trim(),
                    Mailbox = (r.Mailbox ?? "").Trim(),
                    DisplayName = (r.DisplayName ?? "").Trim(),
                    Floor = (r.Floor ?? "").Trim(),
                    Directions = (r.Directions ?? "").Trim(),
                    SignalId = r.SignalId,
                })
                .ToList();

            // Validated as a set, in one go: uniqueness is a property of the
            // whole list, which is why this replaces the array rather than
            // offering a per-room PATCH that could never check it.
            var errors = SettingsValidator.ValidateRooms(rooms);
            if (errors.Count > 0)
            {
                return Results.BadRequest(new AdminValidationResponse(
                    [.. errors.Select(e => new AdminFieldError(e.Field, e.Message))]));
            }

            await store.SaveRoomsAsync(rooms, ct);
            await settings.ReloadAsync(ct);
            await hub.Clients.All.SendAsync("settingsUpdated", ct);

            // The cached window is keyed by room, so a changed directory means
            // the cache is wrong until the next pass. Do it now.
            worker.Wake();

            return Results.Ok(new AdminRoomsRequest(
                [.. settings.Current.Rooms.Select(ToAdminRoom)]));
        });
    }

    private static void MapToolEndpoints(IEndpointRouteBuilder admin)
    {
        admin.MapPost("/graph/test", async (
            GraphConnectionTester tester,
            CancellationToken ct) =>
        {
            var result = await tester.TestAsync(ct);
            return Results.Ok(new AdminTestResponse(result.Ok, result.Message));
        });

        admin.MapPost("/device/test", async (
            AdminDeviceTestRequest request,
            IDeviceBridge device,
            CancellationToken ct) =>
        {
            // Admin-only, so this is not a new public way to poke the hardware.
            // Still a named operation taking a signal id, never a raw command
            // byte — see the comment on DeviceEndpoints.
            await device.ShowDirectionAsync(request.SignalId, ct);
            return Results.Ok(new AdminTestResponse(
                true, $"Indicator {request.SignalId} aangestuurd."));
        });

        admin.MapPost("/sync/now", (CalendarSyncWorker worker) =>
        {
            worker.Wake();
            return Results.Accepted();
        });

        admin.MapGet("/diagnostics", async (
            IMeetingStore store,
            IDeviceBridge device,
            IClock clock,
            ISettingsProvider settings,
            AdminSessionStore sessions,
            IOptions<AdminOptions> options,
            CancellationToken ct) =>
        {
            var status = await store.GetSyncStatusAsync(ct);
            var current = settings.Current;

            // The unsanitised last error lives here rather than on /api/health,
            // which answers an unauthenticated caller.
            return Results.Ok(new AdminDiagnosticsResponse(
                Now: clock.Now,
                ClockSynchronized: clock.IsSynchronized,
                DeviceConnected: device.IsConnected,
                LastSuccessfulSync: status.LastSuccessfulSync,
                LastAttemptedSync: status.LastAttemptedSync,
                LastError: status.LastError,
                MeetingCount: status.MeetingCount,
                Stale: status.IsStale(clock.Now, current.Sync.StaleAfter),
                RoomCount: current.Rooms.Count,
                ActiveSessions: sessions.Count,
                PasswordIsDefault: options.Value.IsDefault));
        });

        admin.MapGet("/settings/export", async (
            ISettingsProvider settings,
            ISettingsStore store,
            IOptions<AdminOptions> options,
            CancellationToken ct) =>
        {
            // A backup and a paste-back-into-rooms.json escape hatch. The secret
            // is not in here, by construction: ToResponse cannot read it.
            var secretIsSet = await store.HasGraphSecretAsync(ct);
            return Results.Ok(ToResponse(settings.Current, secretIsSet, options.Value));
        });
    }

    /// <summary>
    /// Constant-time comparison, so the endpoint does not leak the password one
    /// character at a time through its response time.
    /// </summary>
    private static bool Matches(string? supplied, string expected) =>
        supplied is not null
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied),
            Encoding.UTF8.GetBytes(expected));

    private static AdminSettingsResponse ToResponse(
        KioskSettings settings, bool secretIsSet, AdminOptions admin) =>
        new(
            Graph: new AdminGraphResponse(
                TenantId: settings.Graph.TenantId,
                ClientId: settings.Graph.ClientId,
                BaseUrl: settings.Graph.BaseUrl,
                TimeZone: settings.Graph.TimeZone,
                // No value and no length: a length leaks something.
                SecretIsSet: secretIsSet,
                AuthMode: settings.Graph.SecretIsSet && !secretIsSet
                    ? "certificate"
                    : secretIsSet ? "secret" : "none"),
            Sync: new AdminSyncResponse(
                IntervalMinutes: (int)settings.Sync.Interval.TotalMinutes,
                WindowBehindHours: (int)settings.Sync.WindowBehind.TotalHours,
                WindowAheadHours: (int)settings.Sync.WindowAhead.TotalHours,
                StaleAfterMinutes: (int)settings.Sync.StaleAfter.TotalMinutes,
                RetainPastMeetingsForHours: (int)settings.Sync.RetainPastMeetingsFor.TotalHours),
            Device: new AdminDeviceResponse(
                BusId: settings.Device.BusId,
                Address: settings.Device.Address,
                AttentionPin: settings.Device.AttentionPin,
                ResponseDelayMs: (int)settings.Device.ResponseDelay.TotalMilliseconds,
                MaxRetries: settings.Device.MaxRetries,
                MaxCommandsPerSecond: settings.Device.MaxCommandsPerSecond),
            Privacy: new AdminPrivacyResponse(
                SubjectVisibility: settings.Privacy.SubjectVisibility.ToString(),
                IdleTimeoutSeconds: (int)settings.Privacy.IdleTimeout.TotalSeconds),
            Ui: new AdminUiResponse(
                AttractView: settings.Ui.AttractView.ToString(),
                UpcomingCount: settings.Ui.UpcomingCount,
                ShowAgendaNav: settings.Ui.ShowAgendaNav),
            Rooms: [.. settings.Rooms.Select(ToAdminRoom)],
            PasswordIsDefault: admin.IsDefault);

    private static AdminRoom ToAdminRoom(Room room) =>
        new(room.Id, room.Mailbox, room.DisplayName, room.Floor, room.Directions, room.SignalId);

    /// <summary>
    /// Folds a partial request onto the current settings. A null section means
    /// "leave this alone", so the admin panel can save one tab at a time.
    /// </summary>
    private static KioskSettings Apply(KioskSettings current, AdminSettingsRequest request)
    {
        var graph = request.Graph is { } g
            ? current.Graph with
            {
                TenantId = (g.TenantId ?? current.Graph.TenantId).Trim(),
                ClientId = (g.ClientId ?? current.Graph.ClientId).Trim(),
                BaseUrl = (g.BaseUrl ?? current.Graph.BaseUrl).Trim(),
                TimeZone = (g.TimeZone ?? current.Graph.TimeZone).Trim(),
            }
            : current.Graph;

        var sync = request.Sync is { } s
            ? current.Sync with
            {
                Interval = TimeSpan.FromMinutes(s.IntervalMinutes),
                WindowBehind = TimeSpan.FromHours(s.WindowBehindHours),
                WindowAhead = TimeSpan.FromHours(s.WindowAheadHours),
                StaleAfter = TimeSpan.FromMinutes(s.StaleAfterMinutes),
                RetainPastMeetingsFor = TimeSpan.FromHours(s.RetainPastMeetingsForHours),
            }
            : current.Sync;

        var device = request.Device is { } d
            ? current.Device with
            {
                BusId = d.BusId,
                Address = d.Address,
                AttentionPin = d.AttentionPin,
                ResponseDelay = TimeSpan.FromMilliseconds(d.ResponseDelayMs),
                MaxRetries = d.MaxRetries,
                MaxCommandsPerSecond = d.MaxCommandsPerSecond,
            }
            : current.Device;

        var privacy = request.Privacy is { } p
            ? current.Privacy with
            {
                SubjectVisibility = ParseOr(p.SubjectVisibility, current.Privacy.SubjectVisibility),
                IdleTimeout = TimeSpan.FromSeconds(p.IdleTimeoutSeconds),
            }
            : current.Privacy;

        var ui = request.Ui is { } u
            ? current.Ui with
            {
                AttractView = ParseOr(u.AttractView, current.Ui.AttractView),
                UpcomingCount = u.UpcomingCount,
                ShowAgendaNav = u.ShowAgendaNav,
            }
            : current.Ui;

        return current with
        {
            Graph = graph,
            Sync = sync,
            Device = device,
            Privacy = privacy,
            Ui = ui,
        };
    }

    private static TEnum ParseOr<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    /// <summary>
    /// A room id derived from its name, for the common case of an administrator
    /// who should not have to invent one. Ids appear in device commands and
    /// stored cache rows, so they stay ASCII and lowercase.
    /// </summary>
    private static string Slug(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return $"zaal-{Guid.NewGuid().ToString("n")[..8]}";
        }

        var builder = new StringBuilder(displayName.Length);
        foreach (var c in displayName.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length > 0 ? slug : $"zaal-{Guid.NewGuid().ToString("n")[..8]}";
    }
}

// ---------------------------------------------------------------------------
// Session
// ---------------------------------------------------------------------------

public sealed record AdminLoginRequest(string? Password);

public sealed record AdminSessionResponse(bool Authenticated, bool PasswordIsDefault);

public sealed record AdminErrorResponse(string Error);

public sealed record AdminFieldError(string Field, string Message);

public sealed record AdminValidationResponse(IReadOnlyList<AdminFieldError> Errors);

public sealed record AdminTestResponse(bool Ok, string Message);

// ---------------------------------------------------------------------------
// Settings
// ---------------------------------------------------------------------------

/// <summary>What to do with the client secret on this save.</summary>
public enum SecretAction
{
    /// <summary>Leave the stored secret alone. The default, and what the UI sends
    /// unless someone actually typed a new one.</summary>
    Keep = 0,

    /// <summary>Replace it with <see cref="AdminGraphRequest.Secret"/>.</summary>
    Set = 1,

    /// <summary>Remove it, leaving Outlook unconfigured.</summary>
    Clear = 2,
}

public sealed record AdminGraphRequest(
    string? TenantId,
    string? ClientId,
    string? BaseUrl,
    string? TimeZone,
    SecretAction SecretAction,
    string? Secret);

public sealed record AdminSyncRequest(
    int IntervalMinutes,
    int WindowBehindHours,
    int WindowAheadHours,
    int StaleAfterMinutes,
    int RetainPastMeetingsForHours);

public sealed record AdminDeviceRequest(
    int BusId,
    int Address,
    int? AttentionPin,
    int ResponseDelayMs,
    int MaxRetries,
    int MaxCommandsPerSecond);

public sealed record AdminPrivacyRequest(string? SubjectVisibility, int IdleTimeoutSeconds);

public sealed record AdminUiRequest(string? AttractView, int UpcomingCount, bool ShowAgendaNav);

/// <summary>A partial update: any null section is left as it is.</summary>
public sealed record AdminSettingsRequest(
    AdminGraphRequest? Graph,
    AdminSyncRequest? Sync,
    AdminDeviceRequest? Device,
    AdminPrivacyRequest? Privacy,
    AdminUiRequest? Ui);

/// <summary>
/// Graph settings as read back. There is no Secret property, and there is no
/// code path that could add one: the type that reads settings cannot read the
/// secret.
/// </summary>
public sealed record AdminGraphResponse(
    string TenantId,
    string ClientId,
    string BaseUrl,
    string TimeZone,
    bool SecretIsSet,
    string AuthMode);

public sealed record AdminSyncResponse(
    int IntervalMinutes,
    int WindowBehindHours,
    int WindowAheadHours,
    int StaleAfterMinutes,
    int RetainPastMeetingsForHours);

public sealed record AdminDeviceResponse(
    int BusId,
    int Address,
    int? AttentionPin,
    int ResponseDelayMs,
    int MaxRetries,
    int MaxCommandsPerSecond);

public sealed record AdminPrivacyResponse(string SubjectVisibility, int IdleTimeoutSeconds);

public sealed record AdminUiResponse(string AttractView, int UpcomingCount, bool ShowAgendaNav);

public sealed record AdminRoom(
    string Id,
    string Mailbox,
    string DisplayName,
    string Floor,
    string Directions,
    byte SignalId);

public sealed record AdminRoomsRequest(IReadOnlyList<AdminRoom> Rooms);

public sealed record AdminDeviceTestRequest(byte SignalId);

public sealed record AdminSettingsResponse(
    AdminGraphResponse Graph,
    AdminSyncResponse Sync,
    AdminDeviceResponse Device,
    AdminPrivacyResponse Privacy,
    AdminUiResponse Ui,
    IReadOnlyList<AdminRoom> Rooms,
    bool PasswordIsDefault);

public sealed record AdminDiagnosticsResponse(
    DateTimeOffset Now,
    bool ClockSynchronized,
    bool DeviceConnected,
    DateTimeOffset? LastSuccessfulSync,
    DateTimeOffset? LastAttemptedSync,
    string? LastError,
    int MeetingCount,
    bool Stale,
    int RoomCount,
    int ActiveSessions,
    bool PasswordIsDefault);
