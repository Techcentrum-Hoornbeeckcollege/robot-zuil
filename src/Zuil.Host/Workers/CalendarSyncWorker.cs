using Microsoft.AspNetCore.SignalR;
using Zuil.Core.Abstractions;
using Zuil.Core.Models;
using Zuil.Graph;
using Zuil.Host.Hubs;

namespace Zuil.Host.Workers;

/// <summary>
/// The periodic poll. Reads all configured rooms, then swaps the cache in one
/// transaction.
///
/// Polling rather than Graph change notifications on purpose: webhooks need a
/// publicly reachable HTTPS endpoint, which an internal kiosk on a building LAN
/// does not have. Six requests every few minutes is negligible load.
///
/// Timing comes from the settings snapshot, read once per pass, and the worker
/// also listens for settings changes: pressing Save in the admin panel wakes it
/// within a second or two instead of leaving an administrator watching an empty
/// agenda for up to three minutes wondering whether the credentials took.
/// </summary>
public sealed class CalendarSyncWorker : BackgroundService
{
    private readonly ICalendarSource _source;
    private readonly IMeetingStore _store;
    private readonly IRoomDirectory _rooms;
    private readonly IClock _clock;
    private readonly IHubContext<StatusHub> _hub;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<CalendarSyncWorker> _logger;

    // Set when settings change or an admin asks for a sync now. A
    // TaskCompletionSource rather than a semaphore so a pass that is waiting on
    // the timer and a pass that is waiting to be configured can both be woken by
    // the same signal.
    private volatile TaskCompletionSource _wake =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CalendarSyncWorker(
        ICalendarSource source,
        IMeetingStore store,
        IRoomDirectory rooms,
        IClock clock,
        IHubContext<StatusHub> hub,
        ISettingsProvider settings,
        ILogger<CalendarSyncWorker> logger)
    {
        _source = source;
        _store = store;
        _rooms = rooms;
        _clock = clock;
        _hub = hub;
        _settings = settings;
        _logger = logger;

        _settings.Changed += (_, _) => Wake();
    }

    /// <summary>
    /// Asks for a pass as soon as possible. Called on a settings change and by
    /// the admin panel's "Nu synchroniseren".
    /// </summary>
    public void Wake() => _wake.TrySetResult();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't sync against a clock we don't trust yet; see SystemClock.
        while (!_clock.IsSynchronized && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Waiting for NTP synchronisation before first sync");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Reset before the pass, so a change arriving during it still wakes
            // the next one rather than being swallowed.
            var wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _wake = wake;

            var configured = await SyncOnceAsync(stoppingToken);

            // Unconfigured means there is nothing to retry: wait to be told that
            // credentials have been entered rather than failing on a timer
            // forever. A long backstop in case the signal is somehow missed.
            var delay = configured
                ? _settings.Current.Sync.Interval
                : TimeSpan.FromMinutes(30);

            await WaitAsync(wake, delay, stoppingToken);
        }
    }

    /// <summary>
    /// Runs one pass. Returns false when Outlook is not configured, which is a
    /// state to wait in rather than an error to retry.
    /// </summary>
    private async Task<bool> SyncOnceAsync(CancellationToken ct)
    {
        // One snapshot for the whole pass. Re-reading mid-pass could mix two room
        // lists or two window sizes into a single cache swap.
        var settings = _settings.Current;
        var sync = settings.Sync;

        var now = _clock.Now;
        var from = now - sync.WindowBehind;
        var to = now + sync.WindowAhead;

        var rooms = _rooms.All;
        if (rooms.Count == 0)
        {
            _logger.LogInformation("No rooms configured yet; skipping sync");
            await _store.RecordSyncAttemptAsync(
                now, "Er zijn nog geen zalen ingesteld.", CancellationToken.None);
            return false;
        }

        try
        {
            var all = new List<Meeting>();

            foreach (var room in rooms)
            {
                // One room failing must not cost us the other five, so a per-room
                // failure aborts the whole swap rather than caching a partial
                // building. Losing one room silently is worse than staying stale.
                var occurrences = await _source.GetOccurrencesAsync(room, from, to, ct);
                all.AddRange(occurrences);
            }

            await _store.ReplaceWindowAsync(from, to, all, ct);
            await _store.PurgeBeforeAsync(now - sync.RetainPastMeetingsFor, ct);

            await _hub.Clients.All.SendAsync("cacheUpdated", new
            {
                syncedAt = now,
                meetingCount = all.Count,
            }, ct);

            _logger.LogInformation(
                "Synced {Count} occurrences across {Rooms} rooms", all.Count, rooms.Count);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GraphNotConfiguredException)
        {
            // Information, not Error: this is the expected state of a kiosk that
            // has been plugged in but not configured, and it must not fill the
            // journal with stack traces while it waits.
            _logger.LogInformation("Outlook is not configured yet; waiting for credentials");
            await _store.RecordSyncAttemptAsync(
                now,
                "Outlook is nog niet ingesteld. Vul de gegevens in onder Instellingen → Outlook.",
                CancellationToken.None);
            return false;
        }
        catch (Exception ex)
        {
            // Keep serving the previous cache. The kiosk stays useful; the status
            // endpoint and the UI banner report that data is ageing.
            _logger.LogError(ex, "Calendar sync failed; serving previous cache");

            // Sanitised: /api/health shows this to an unauthenticated caller, and
            // Graph and MSAL messages can carry a response body. The full
            // exception is in the log above, and the admin diagnostics page.
            await _store.RecordSyncAttemptAsync(now, Describe(ex), CancellationToken.None);
            return true;
        }
    }

    /// <summary>
    /// A short, safe description of a sync failure, fit for an unauthenticated
    /// status endpoint.
    /// </summary>
    private static string Describe(Exception ex) => ex switch
    {
        GraphAuthenticationException auth => auth.Message,
        GraphRequestException => "Outlook gaf een foutmelding op het ophalen van een agenda.",
        HttpRequestException => "Geen verbinding met Outlook.",
        TaskCanceledException => "De verbinding met Outlook duurde te lang.",
        TimeZoneNotFoundException => "De ingestelde tijdzone bestaat niet op dit apparaat.",
        _ => "Synchroniseren is niet gelukt.",
    };

    /// <summary>
    /// Waits for whichever comes first: the interval elapsing, someone pressing
    /// Save, or shutdown.
    /// </summary>
    private static async Task WaitAsync(
        TaskCompletionSource wake, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            var timer = Task.Delay(delay, ct);
            await Task.WhenAny(timer, wake.Task);
        }
        catch (OperationCanceledException)
        {
            // Shutdown; the loop condition handles it.
        }
    }
}
