using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Reflection;
using System.Threading.RateLimiting;
using Zuil.Core.Abstractions;
using Zuil.Core.Admin;
using Zuil.Device;
using Zuil.Graph;
using Zuil.Host;
using Zuil.Host.Admin;
using Zuil.Host.Endpoints;
using Zuil.Host.Hubs;
using Zuil.Host.Settings;
using Zuil.Host.Workers;
using Zuil.Storage;

// Development switches so the whole kiosk runs on a laptop with no tenant and
// no hardware. Without these, every UI tweak would need the Pi on the bench.
var useFakeCalendar = args.Contains("--fake-calendar");
var useFakeDevice = args.Contains("--fake-device");
var noKiosk = args.Contains("--no-kiosk");

var builder = WebApplication.CreateBuilder(args);

// There are no configuration files and no environment variables. Everything an
// operator can set is in the database and editable from the GUI; the only thing
// that cannot be, because it is where the settings live, is the database's own
// location — and that is decided by rules rather than by a file somebody has to
// write. See Zuil.Storage.DataDirectory.
//
// This all happens before builder.Build(), because the port Kestrel binds is
// itself one of the settings.
// Logging defaults live here rather than in a settings file, because a
// single exe means exactly one file: nothing beside it to copy, lose, or forget
// to update. systemd captures stdout, so the console provider is the whole
// story on the Pi.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(console => console.TimestampFormat = "HH:mm:ss ");
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

using var bootstrapLoggers = LoggerFactory.Create(logging =>
{
    logging.AddSimpleConsole(console => console.TimestampFormat = "HH:mm:ss ");
    logging.SetMinimumLevel(LogLevel.Information);
});

var dataDirectory = DataDirectory.Resolve(bootstrapLoggers.CreateLogger(typeof(DataDirectory)));

var database = new ZuilDatabase(dataDirectory, bootstrapLoggers.CreateLogger<ZuilDatabase>());

var secretProtector = new AesGcmSecretProtector(
    database.SecretKeyPath,
    bootstrapLoggers.CreateLogger<AesGcmSecretProtector>());

var settingsStore = new SqliteSettingsStore(
    database, secretProtector, bootstrapLoggers.CreateLogger<SqliteSettingsStore>());

// Seed admin/password on first run. Weak on purpose so a fresh kiosk is
// reachable; the panel warns until it is changed.
await AdminBootstrap.EnsureCredentialAsync(
    settingsStore, bootstrapLoggers.CreateLogger(typeof(AdminBootstrap)), CancellationToken.None);

var startupSettings = await settingsStore.LoadAsync(CancellationToken.None);

// Loopback only, always. NetworkSettings.Host is a constant and no request can
// change it: putting the admin panel on the building network over plain HTTP
// would leave whatever password it currently has as the only thing in the way.
builder.WebHost.UseUrls(startupSettings.Network.Url);

builder.Services.AddSignalR();
builder.Services.AddSingleton<IClock, SystemClock>();

// Registered as the very instances built above, so nothing opens the database
// twice.
builder.Services.AddSingleton(database);
builder.Services.AddSingleton(secretProtector);
builder.Services.AddSingleton(settingsStore);

// One instance behind two interfaces: ISettingsStore is what the admin panel
// gets and cannot read the Graph secret, IGraphCredentialSource is the only
// thing that can. See the comments on those interfaces.
builder.Services.AddSingleton<ISettingsStore>(
    sp => sp.GetRequiredService<SqliteSettingsStore>());
builder.Services.AddSingleton<IGraphCredentialSource>(
    sp => sp.GetRequiredService<SqliteSettingsStore>());
builder.Services.AddSingleton<ISettingsProvider, SettingsProvider>();

// Admin sessions are in-process, so a restart signs everyone out. On a machine
// in a lobby that is a feature. The idle timeout is read per check, because it
// is a setting the panel can change.
builder.Services.AddSingleton(sp => new AdminSessionStore(
    sp.GetRequiredService<IClock>(),
    () => sp.GetRequiredService<ISettingsProvider>().Current.Security.SessionIdleTimeout));

// Login is the one route worth rate limiting. Shared-framework feature, so no
// package reference; the persistent lockout in the database is the part that
// survives a power cycle.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AdminEndpoints.LoginRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));
});

// Room directory: a snapshot over the settings provider, rebuilt whenever an
// administrator edits the rooms.
builder.Services.AddSingleton<IRoomDirectory, SqliteBackedRoomDirectory>();

builder.Services.AddSingleton<IMeetingStore, SqliteMeetingStore>();

// The token provider is registered either way: the admin panel's "test
// connection" needs it even on a kiosk started with --fake-calendar.
builder.Services.AddSingleton<GraphTokenProvider>();

// Registered even under --fake-calendar: "test connection" in the admin panel
// exists precisely to exercise the real path.
builder.Services.AddHttpClient<GraphConnectionTester>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(10));

if (useFakeCalendar)
{
    builder.Services.AddSingleton<ICalendarSource, FakeCalendarSource>();
}
else
{
    builder.Services.AddHttpClient<ICalendarSource, CalendarViewClient>()
        .SetHandlerLifetime(TimeSpan.FromMinutes(10));
}

builder.Services.AddSingleton<CommandPolicy>();

if (useFakeDevice || !File.Exists("/dev/i2c-1"))
{
    // No i2c bus present means a dev machine. Falling back rather than crashing
    // keeps `dotnet run` working on a laptop with no flags.
    builder.Services.AddSingleton<IDeviceBridge, FakeBridge>();
}
else
{
    builder.Services.AddSingleton<IDeviceBridge, Esp32I2cBridge>();
}

// Registered as a singleton as well as a hosted service, so the admin panel's
// "Nu synchroniseren" can wake the very instance the host is running.
builder.Services.AddSingleton<CalendarSyncWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CalendarSyncWorker>());

var app = builder.Build();

// Say where we are reading and writing, every start. On a wall-mounted machine
// the journal is the only way to tell which data directory won.
app.Services.GetRequiredService<ILogger<Program>>().LogInformation(
    "Database {DatabasePath}, listening on {Url}",
    database.DatabasePath,
    startupSettings.Network.Url);

// Put the six example rooms in on the very first run, so a new kiosk has
// something to show while it is being set up. Guarded by a marker, so an
// administrator who deletes them all does not find them back.
await FirstRunSeeder.SeedAsync(
    app.Services.GetRequiredService<ISettingsStore>(),
    app.Services.GetRequiredService<ILogger<Program>>(),
    CancellationToken.None);

await app.Services.GetRequiredService<ISettingsProvider>().ReloadAsync(CancellationToken.None);

// The frontend is served from embedded resources, which is what makes the
// deployable a single file rather than a binary plus a wwwroot folder.
//
// A publish with an empty wwwroot leaves no manifest entry for the directory at
// all, and ManifestEmbeddedFileProvider throws on construction rather than
// coming back empty — which used to take the whole process down on boot, before
// Kestrel bound a port, so the one thing that could have said what was wrong
// never got the chance. Staying up is worth more: the fallback then answers
// every request with the build command to run. See FallbackEndpoints.
IFileProvider embedded;

try
{
    embedded = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "wwwroot");
}
catch (InvalidOperationException ex)
{
    app.Services.GetRequiredService<ILogger<Program>>().LogError(
        ex,
        "No frontend is embedded in this binary. The API will answer, but every "
        + "page will explain how to build the UI instead of showing it. Run "
        + "build/build.sh, or use the Vite dev server");

    embedded = new NullFileProvider();
}

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embedded });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = embedded,
    OnPrepareResponse = ctx =>
    {
        // Vite fingerprints assets, so they cache forever; index.html must not,
        // or a kiosk left running for a month never picks up a deploy.
        var path = ctx.File.Name;
        ctx.Context.Response.Headers.CacheControl =
            path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                ? "no-cache, no-store"
                : "public, max-age=31536000, immutable";
    },
});

app.UseRateLimiter();

app.MapSearchEndpoints();
app.MapRoomEndpoints();
app.MapAgendaEndpoints();
app.MapDeviceEndpoints();
app.MapHealthEndpoints();
app.MapAdminEndpoints();
app.MapHub<StatusHub>("/hub/status");

// SPA fallback: any non-API route hands back index.html so client-side routing
// survives a refresh on the kiosk. Everything the fallback has to tell a caller
// instead of a page — an unknown endpoint, a missing frontend — is in there.
app.MapFrontendFallback(embedded);

// Ping the column once at startup so a wiring fault shows up in the logs now,
// rather than the first time a visitor searches.
_ = Task.Run(async () =>
{
    var device = app.Services.GetRequiredService<IDeviceBridge>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    if (await device.PingAsync(CancellationToken.None))
    {
        logger.LogInformation("Device responded to ping");
    }
    else
    {
        logger.LogError("Device did not respond to ping; check wiring and i2c address");
    }
});

if (!noKiosk)
{
    LaunchKiosk(app);
}

app.Run();

/// <summary>
/// Opens the local UI full-screen in Chromium.
///
/// Only used when the app is not managed by systemd; on the Pi,
/// deploy/zuil-kiosk.service owns the browser so that it can be restarted
/// independently of the backend.
/// </summary>
static void LaunchKiosk(WebApplication app)
{
    var settings = app.Services.GetRequiredService<ISettingsProvider>().Current;

    if (!settings.Browser.LaunchBrowser)
    {
        return;
    }

    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = settings.Browser.BrowserPath,
                ArgumentList =
                {
                    "--kiosk",
                    $"--app={settings.Network.Url}",
                    // Nothing persists between visitors: the last person's name
                    // and meeting must not be recoverable from the browser.
                    "--incognito",
                    "--noerrdialogs",
                    "--disable-infobars",
                    "--disable-session-crashed-bubble",
                    "--disable-pinch",
                    "--overscroll-history-navigation=0",
                    "--check-for-update-interval=31536000",
                },
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Could not launch kiosk browser at {Path}", settings.Browser.BrowserPath);
        }
    });
}
