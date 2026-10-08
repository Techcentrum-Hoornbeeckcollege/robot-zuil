using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
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

// Configuration lives in ./config next to the binary, not inside it: room
// directions change with the building, and credentials must never be compiled in.
var baseDir = AppContext.BaseDirectory;
var configDir = Path.Combine(baseDir, "config");

builder.Configuration
    .AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(
        Path.Combine(configDir, $"appsettings.{builder.Environment.EnvironmentName}.json"),
        optional: true,
        reloadOnChange: true)
    .AddEnvironmentVariables("ZUIL_");

// Graph credentials, sync timing and device parameters now live in the database
// and are edited from the admin panel, so they are no longer bound here. What
// remains in configuration is what cannot change without a restart anyway.
builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<KioskOptions>(
    builder.Configuration.GetSection(KioskOptions.SectionName));

// The admin password stays in configuration, not the database: a password a
// visitor could change from the touchscreen is not a password.
builder.Services.Configure<AdminOptions>(
    builder.Configuration.GetSection(AdminOptions.SectionName));

// The certificate escape hatch stays file-only and wins over a stored secret,
// so a site already following docs/outlook-setup.md loses nothing by upgrading.
builder.Services.Configure<GraphCredentialOptions>(
    builder.Configuration.GetSection(GraphCredentialOptions.SectionName));

builder.Services.AddSignalR();
builder.Services.AddSingleton<IClock, SystemClock>();

// One connection and one write lock, shared by both stores. A second connection
// would reintroduce the SQLITE_BUSY contention this arrangement avoids.
builder.Services.AddSingleton<ZuilDatabase>();

// The key lives beside the database, not in the single-file extraction
// directory, which is versioned per binary hash and wiped on the next deploy.
builder.Services.AddSingleton(sp => new AesGcmSecretProtector(
    sp.GetRequiredService<ZuilDatabase>().SecretKeyPath,
    sp.GetRequiredService<ILogger<AesGcmSecretProtector>>()));

// One instance behind two interfaces: ISettingsStore is what the admin panel
// gets and cannot read the Graph secret, IGraphCredentialSource is the only
// thing that can. See the comments on those interfaces.
builder.Services.AddSingleton<SqliteSettingsStore>();
builder.Services.AddSingleton<ISettingsStore>(
    sp => sp.GetRequiredService<SqliteSettingsStore>());
builder.Services.AddSingleton<IGraphCredentialSource>(
    sp => sp.GetRequiredService<SqliteSettingsStore>());
builder.Services.AddSingleton<ISettingsProvider, SettingsProvider>();

// Admin sessions are in-process, so a restart signs everyone out. On a machine
// in a lobby that is a feature.
builder.Services.AddSingleton(sp => new AdminSessionStore(
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<IOptions<AdminOptions>>().Value.SessionIdleTimeout));

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
// administrator edits the rooms. config/rooms.json is only the first-run seed.
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

// Settings live in the database now. Carry an existing install's config files
// across on the first boot, then load the snapshot everything else reads from.
// Both before the sync worker's first pass, so it never runs against defaults.
await FirstRunSeeder.SeedAsync(
    app.Services.GetRequiredService<ISettingsStore>(),
    builder.Configuration,
    Path.Combine(configDir, "rooms.json"),
    app.Services.GetRequiredService<ILogger<Program>>(),
    CancellationToken.None);

await app.Services.GetRequiredService<ISettingsProvider>().ReloadAsync(CancellationToken.None);

// The frontend is served from embedded resources, which is what makes the
// deployable a single file rather than a binary plus a wwwroot folder.
var embedded = new ManifestEmbeddedFileProvider(
    Assembly.GetExecutingAssembly(), "wwwroot");

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
// survives a refresh on the kiosk.
app.MapFallback(async context =>
{
    var file = embedded.GetFileInfo("index.html");

    if (!file.Exists)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsync(
            "Frontend not built. Run build/build.sh, or use the Vite dev server.");
        return;
    }

    context.Response.ContentType = "text/html";
    context.Response.Headers.CacheControl = "no-cache, no-store";
    await context.Response.SendFileAsync(file);
});

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
    var options = app.Services
        .GetRequiredService<IOptions<KioskOptions>>().Value;

    if (!options.LaunchBrowser)
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
                FileName = options.BrowserPath,
                ArgumentList =
                {
                    "--kiosk",
                    $"--app={options.Url}",
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
            logger.LogError(ex, "Could not launch kiosk browser at {Path}", options.BrowserPath);
        }
    });
}
