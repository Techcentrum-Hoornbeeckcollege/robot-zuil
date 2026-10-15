using Microsoft.AspNetCore.SignalR;
using System.Text;
using Zuil.Core.Abstractions;
using Zuil.Core.Devices;
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
        app.MapGet("/api/admin/session", async (
            HttpContext context,
            AdminSessionStore sessions,
            ISettingsStore store,
            CancellationToken ct) =>
        {
            var token = AdminSession.TokenFrom(context.Request);
            var authenticated = token is not null && sessions.Validate(AdminSession.Hash(token));
            var credential = await store.GetAdminCredentialAsync(ct);

            return Results.Ok(new AdminSessionResponse(
                Authenticated: authenticated,
                CredentialsAreDefault: credential?.IsDefault ?? true,
                Username: credential?.Username ?? AdminCredential.DefaultUsername));
        });

        app.MapPost("/api/admin/session", async (
            AdminLoginRequest request,
            HttpContext context,
            AdminSessionStore sessions,
            ISettingsStore store,
            ISettingsProvider settings,
            IClock clock,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(AdminLogCategory);
            var security = settings.Current.Security;
            var now = clock.Now;

            // The persistent ledger is what makes the lockout survive someone
            // pulling the plug, which an in-memory counter does not.
            var failures = await store.CountFailedLoginsAsync(now - security.LockoutWindow, ct);
            if (failures >= security.MaxFailedAttempts)
            {
                logger.LogWarning(
                    "Admin login refused: locked out after {Failures} failures from {Ip}",
                    failures, context.Connection.RemoteIpAddress);

                return Results.Json(
                    new AdminErrorResponse(
                        "Te veel mislukte pogingen. Probeer het over een paar minuten opnieuw."),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var credential = await store.GetAdminCredentialAsync(ct);

            // Verify the password even when the username is wrong, so the two
            // failures cost the same and the endpoint does not become a way to
            // enumerate usernames.
            var usernameMatches = credential is not null
                && string.Equals(
                    request.Username?.Trim(), credential.Username, StringComparison.OrdinalIgnoreCase);
            var passwordMatches = credential is not null
                && PasswordHasher.Verify(request.Password ?? "", credential.PasswordHash);

            if (!usernameMatches || !passwordMatches)
            {
                await store.RecordLoginAttemptAsync(now, succeeded: false, ct);

                // Never the attempted credentials, in a log an administrator may
                // well paste into a support ticket.
                logger.LogWarning(
                    "Admin login failed from {Ip}", context.Connection.RemoteIpAddress);

                // A fixed cost on failure, so the endpoint is not a fast oracle.
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);

                // One message for both, so it says nothing about which was wrong.
                return Results.Json(
                    new AdminErrorResponse("Onjuiste gebruikersnaam of wachtwoord."),
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
                CredentialsAreDefault: credential!.IsDefault,
                Username: credential.Username));
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
            CancellationToken ct) =>
        {
            var secretIsSet = await store.HasGraphSecretAsync(ct);
            var credential = await store.GetAdminCredentialAsync(ct);

            return Results.Ok(ToResponse(settings.Current, secretIsSet, credential));
        });

        admin.MapPut("/settings", async (
            AdminSettingsRequest request,
            ISettingsProvider settings,
            ISettingsStore store,
            GraphTokenProvider tokens,
            CalendarSyncWorker worker,
            IHubContext<StatusHub> hub,
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

            switch (request.Graph?.CertificatePasswordAction ?? SecretAction.Keep)
            {
                case SecretAction.Set:
                    await store.SetCertificatePasswordAsync(
                        request.Graph!.CertificatePassword, ct);
                    break;
                case SecretAction.Clear:
                    await store.SetCertificatePasswordAsync(null, ct);
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
            var credential = await store.GetAdminCredentialAsync(ct);
            return Results.Ok(ToResponse(settings.Current, secretIsSet, credential));
        });

        // Changing the credentials is its own endpoint rather than part of the
        // settings PUT: it needs the current password, and a password hash has
        // no business travelling in the same payload as the sync interval.
        admin.MapPut("/credentials", async (
            AdminCredentialRequest request,
            ISettingsStore store,
            AdminSessionStore sessions,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(AdminLogCategory);
            var current = await store.GetAdminCredentialAsync(ct);

            if (current is null)
            {
                return Results.Problem("Er is nog geen beheeraccount.");
            }

            // Being logged in is not enough: an unattended open panel must not
            // be a way to take the account over.
            if (!PasswordHasher.Verify(request.CurrentPassword ?? "", current.PasswordHash))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);

                return Results.BadRequest(new AdminValidationResponse(
                    [new AdminFieldError("currentPassword", "Huidig wachtwoord klopt niet.")]));
            }

            var username = (request.Username ?? "").Trim();
            var errors = new List<AdminFieldError>();

            if (username.Length < 3)
            {
                errors.Add(new AdminFieldError(
                    "username", "Gebruikersnaam moet minstens 3 tekens zijn."));
            }

            if ((request.NewPassword ?? "").Length < 8)
            {
                errors.Add(new AdminFieldError(
                    "newPassword", "Wachtwoord moet minstens 8 tekens zijn."));
            }
            else if (string.Equals(
                request.NewPassword, AdminCredential.DefaultPassword, StringComparison.Ordinal))
            {
                errors.Add(new AdminFieldError(
                    "newPassword", "Kies iets anders dan het standaardwachtwoord."));
            }

            if (errors.Count > 0)
            {
                return Results.BadRequest(new AdminValidationResponse(errors));
            }

            await store.SetAdminCredentialAsync(
                new AdminCredential(
                    Username: username,
                    PasswordHash: PasswordHasher.Hash(request.NewPassword!),
                    IsDefault: false),
                ct);

            // Every other session was opened with the old password. Whoever
            // changed it keeps working; anyone else is signed out.
            sessions.Clear();

            logger.LogInformation("Admin credentials changed; all sessions ended");

            return Results.Ok(new AdminTestResponse(
                true, "Opgeslagen. U moet opnieuw inloggen."));
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

    private static AdminDeviceCapabilityResponse ToAdminCapability(DeviceCapability capability) =>
        new(
            capability.Key,
            capability.Label,
            $"0x{(byte)capability.Command:X2}",
            capability.Hint,
            capability.ExpectsReply,
            [.. capability.Parameters.Select(parameter => new AdminDeviceParameterResponse(
                parameter.Key,
                parameter.Label,
                parameter.Kind.ToString(),
                parameter.Minimum,
                parameter.Maximum,
                parameter.Step,
                parameter.Default,
                parameter.Unit,
                parameter.Hint,
                [.. parameter.Choices.Select(choice =>
                    new AdminDeviceChoiceResponse(choice.Value, choice.Label))]))]);

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

        // The catalogue, so the admin screen can draw a control per parameter
        // without being taught what the parameters mean. Admin-only: it is a
        // description of the protocol, which is not something to hand to a
        // visitor's browser.
        admin.MapGet("/device/capabilities", () =>
            Results.Ok(new AdminDeviceCapabilitiesResponse(
                [.. DeviceCatalogue.All.Select(ToAdminCapability)])));

        admin.MapPost("/device/command", async (
            AdminDeviceCommandRequest request,
            IDeviceBridge device,
            CancellationToken ct) =>
        {
            // Still not a raw command byte: the key names a declared capability,
            // and every value is checked against that declaration before a frame
            // exists. An unknown key is a 404, not an opportunity.
            if (DeviceCatalogue.Find(request.Command) is not { } capability)
            {
                return Results.NotFound(new AdminTestResponse(
                    false, $"Onbekend commando: {request.Command}."));
            }

            var values = request.Values ?? new Dictionary<string, long>(StringComparer.Ordinal);

            if (!capability.TryBuild(values, out var instruction, out var invalid))
            {
                return Results.BadRequest(new AdminValidationResponse(
                    [.. invalid.Select(error => new AdminFieldError(error.Parameter, error.Message))]));
            }

            var reply = await device.SendAsync(instruction, ct);

            // A null reply is the normal shape of "the bus did not answer", so it
            // reports as a failed command rather than as a server error.
            return Results.Ok(reply is null
                ? new AdminDeviceReplyResponse(
                    false,
                    "Geen antwoord van de zuil. Controleer de bedrading en het i2c-adres.",
                    null,
                    null,
                    null,
                    null)
                : new AdminDeviceReplyResponse(
                    reply.IsSuccess,
                    reply.IsSuccess
                        ? $"{capability.Label}: {reply.Name} in {reply.RoundTrip.TotalMilliseconds:F0} ms."
                        : $"{capability.Label}: de zuil antwoordde {reply.Name}.",
                    reply.Name,
                    Convert.ToHexString(reply.Payload.Span),
                    (int)reply.RoundTrip.TotalMilliseconds,
                    reply.Attempts));
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
            Zuil.Storage.ZuilDatabase database,
            AdminSessionStore sessions,
            ISettingsStore settingsStore,
            CancellationToken ct) =>
        {
            var status = await store.GetSyncStatusAsync(ct);
            var current = settings.Current;
            var credential = await settingsStore.GetAdminCredentialAsync(ct);

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
                CredentialsAreDefault: credential?.IsDefault ?? true,
                DataDirectory: System.IO.Path.GetDirectoryName(database.DatabasePath) ?? "",
                Status: HealthEvaluation.Evaluate(
                    status,
                    clock.IsSynchronized,
                    device.IsConnected,
                    clock.Now,
                    current.Sync.StaleAfter).ToString()));
        });

        admin.MapGet("/settings/export", async (
            ISettingsProvider settings,
            ISettingsStore store,
            CancellationToken ct) =>
        {
            // A backup of everything the GUI can set. Neither the Graph secret
            // nor the password hash is in here, by construction: ToResponse
            // cannot read either.
            var secretIsSet = await store.HasGraphSecretAsync(ct);
            var credential = await store.GetAdminCredentialAsync(ct);
            return Results.Ok(ToResponse(settings.Current, secretIsSet, credential));
        });
    }

    private static AdminSettingsResponse ToResponse(
        KioskSettings settings, bool secretIsSet, AdminCredential? credential) =>
        new(
            Graph: new AdminGraphResponse(
                TenantId: settings.Graph.TenantId,
                ClientId: settings.Graph.ClientId,
                BaseUrl: settings.Graph.BaseUrl,
                TimeZone: settings.Graph.TimeZone,
                CertificatePath: settings.Graph.CertificatePath,
                CertificatePasswordIsSet: settings.Graph.CertificatePasswordIsSet,
                // No value and no length: a length leaks something.
                SecretIsSet: secretIsSet,
                // The certificate wins when one is configured, which is what
                // GraphTokenProvider does.
                AuthMode: !string.IsNullOrWhiteSpace(settings.Graph.CertificatePath)
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
            Security: new AdminSecurityResponse(
                SessionIdleTimeoutMinutes: (int)settings.Security.SessionIdleTimeout.TotalMinutes,
                MaxFailedAttempts: settings.Security.MaxFailedAttempts,
                LockoutWindowMinutes: (int)settings.Security.LockoutWindow.TotalMinutes),
            Network: new AdminNetworkResponse(
                Host: NetworkSettings.Host,
                Port: settings.Network.Port),
            Browser: new AdminBrowserResponse(
                LaunchBrowser: settings.Browser.LaunchBrowser,
                BrowserPath: settings.Browser.BrowserPath),
            Rooms: [.. settings.Rooms.Select(ToAdminRoom)],
            Username: credential?.Username ?? AdminCredential.DefaultUsername,
            CredentialsAreDefault: credential?.IsDefault ?? true);

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
                CertificatePath = (g.CertificatePath ?? current.Graph.CertificatePath).Trim(),
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

        var security = request.Security is { } sec
            ? current.Security with
            {
                SessionIdleTimeout = TimeSpan.FromMinutes(sec.SessionIdleTimeoutMinutes),
                MaxFailedAttempts = sec.MaxFailedAttempts,
                LockoutWindow = TimeSpan.FromMinutes(sec.LockoutWindowMinutes),
            }
            : current.Security;

        // Only the port. The host is a constant, so no request can move the
        // admin panel onto the building network.
        var network = request.Network is { } net
            ? current.Network with { Port = net.Port }
            : current.Network;

        var browser = request.Browser is { } br
            ? current.Browser with
            {
                LaunchBrowser = br.LaunchBrowser,
                BrowserPath = (br.BrowserPath ?? current.Browser.BrowserPath).Trim(),
            }
            : current.Browser;

        return current with
        {
            Graph = graph,
            Sync = sync,
            Device = device,
            Privacy = privacy,
            Ui = ui,
            Security = security,
            Network = network,
            Browser = browser,
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

public sealed record AdminLoginRequest(string? Username, string? Password);

public sealed record AdminSessionResponse(
    bool Authenticated,
    bool CredentialsAreDefault,
    string Username);

/// <summary>Changing the admin account. Needs the current password.</summary>
public sealed record AdminCredentialRequest(
    string? Username,
    string? CurrentPassword,
    string? NewPassword);

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
    string? CertificatePath,
    SecretAction SecretAction,
    string? Secret,
    SecretAction CertificatePasswordAction,
    string? CertificatePassword);

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

public sealed record AdminSecurityRequest(
    int SessionIdleTimeoutMinutes,
    int MaxFailedAttempts,
    int LockoutWindowMinutes);

/// <summary>Port only; the host is not settable.</summary>
public sealed record AdminNetworkRequest(int Port);

public sealed record AdminBrowserRequest(bool LaunchBrowser, string? BrowserPath);

/// <summary>A partial update: any null section is left as it is.</summary>
public sealed record AdminSettingsRequest(
    AdminGraphRequest? Graph,
    AdminSyncRequest? Sync,
    AdminDeviceRequest? Device,
    AdminPrivacyRequest? Privacy,
    AdminUiRequest? Ui,
    AdminSecurityRequest? Security,
    AdminNetworkRequest? Network,
    AdminBrowserRequest? Browser);

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
    string CertificatePath,
    bool CertificatePasswordIsSet,
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

public sealed record AdminSecurityResponse(
    int SessionIdleTimeoutMinutes,
    int MaxFailedAttempts,
    int LockoutWindowMinutes);

/// <summary>Host is reported so the UI can show it as fixed, never as an input.</summary>
public sealed record AdminNetworkResponse(string Host, int Port);

public sealed record AdminBrowserResponse(bool LaunchBrowser, string BrowserPath);

public sealed record AdminRoom(
    string Id,
    string Mailbox,
    string DisplayName,
    string Floor,
    string Directions,
    byte SignalId);

public sealed record AdminRoomsRequest(IReadOnlyList<AdminRoom> Rooms);

public sealed record AdminDeviceTestRequest(byte SignalId);

/// <summary>
/// A device command as the admin screen describes it, flattened from
/// <see cref="DeviceCapability"/> so the browser gets plain JSON.
/// </summary>
public sealed record AdminDeviceCapabilityResponse(
    string Key,
    string Label,
    string Code,
    string? Hint,
    bool ExpectsReply,
    IReadOnlyList<AdminDeviceParameterResponse> Parameters);

public sealed record AdminDeviceParameterResponse(
    string Key,
    string Label,
    string Kind,
    long Minimum,
    long Maximum,
    long Step,
    long Default,
    string Unit,
    string? Hint,
    IReadOnlyList<AdminDeviceChoiceResponse> Choices);

public sealed record AdminDeviceChoiceResponse(long Value, string Label);

public sealed record AdminDeviceCapabilitiesResponse(
    IReadOnlyList<AdminDeviceCapabilityResponse> Capabilities);

/// <summary>
/// Values keyed by parameter. Deliberately not a payload: the host builds the
/// bytes from the declaration, so a browser cannot choose what goes on the bus.
/// </summary>
public sealed record AdminDeviceCommandRequest(
    string? Command,
    Dictionary<string, long>? Values);

public sealed record AdminDeviceReplyResponse(
    bool Ok,
    string Message,
    string? Response,
    string? Payload,
    int? RoundTripMs,
    int? Attempts);


public sealed record AdminSettingsResponse(
    AdminGraphResponse Graph,
    AdminSyncResponse Sync,
    AdminDeviceResponse Device,
    AdminPrivacyResponse Privacy,
    AdminUiResponse Ui,
    AdminSecurityResponse Security,
    AdminNetworkResponse Network,
    AdminBrowserResponse Browser,
    IReadOnlyList<AdminRoom> Rooms,
    string Username,
    bool CredentialsAreDefault);

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
    bool CredentialsAreDefault,
    string DataDirectory,
    string Status);
