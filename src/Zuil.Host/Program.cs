using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Reflection;
using Zuil.Core.Abstractions;
using Zuil.Device;
using Zuil.Graph;
using Zuil.Host;
using Zuil.Host.Endpoints;
using Zuil.Host.Hubs;
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

builder.Services.AddOptions<GraphOptions>()
    .Bind(builder.Configuration.GetSection(GraphOptions.SectionName))
    .ValidateOnStart();
builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<DeviceOptions>(
    builder.Configuration.GetSection(DeviceOptions.SectionName));
builder.Services.Configure<SyncOptions>(
    builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.Configure<KioskOptions>(
    builder.Configuration.GetSection(KioskOptions.SectionName));

builder.Services.AddSignalR();
builder.Services.AddSingleton<IClock, SystemClock>();

// Room directory: read once at startup, and fail fast if it is missing or
// inconsistent rather than serving a kiosk that points nowhere.
builder.Services.AddSingleton<IRoomDirectory>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<RoomDirectory>>();
    var path = Path.Combine(configDir, "rooms.json");
    return RoomDirectory.LoadFromFile(path, logger);
});

builder.Services.AddSingleton<IMeetingStore, SqliteMeetingStore>();

if (useFakeCalendar)
{
    builder.Services.AddSingleton<ICalendarSource, FakeCalendarSource>();
}
else
{
    builder.Services.AddSingleton<GraphTokenProvider>();
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

builder.Services.AddHostedService<CalendarSyncWorker>();

var app = builder.Build();

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

app.MapSearchEndpoints();
app.MapRoomEndpoints();
app.MapDeviceEndpoints();
app.MapHealthEndpoints();
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
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<KioskOptions>>().Value;

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
