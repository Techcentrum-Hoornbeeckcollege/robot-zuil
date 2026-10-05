using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Zuil.Core.Abstractions;
using Zuil.Core.Models;
using Zuil.Host.Hubs;

namespace Zuil.Host.Workers;

public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>How often to re-read all six room calendars.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>How far back the cached window starts (catches in-progress meetings).</summary>
    public TimeSpan WindowBehind { get; set; } = TimeSpan.FromHours(2);

    /// <summary>How far ahead to cache. Today plus tomorrow is plenty for wayfinding.</summary>
    public TimeSpan WindowAhead { get; set; } = TimeSpan.FromHours(36);

    /// <summary>Cache older than this makes the kiosk show a staleness warning.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// The periodic poll. Reads all configured rooms, then swaps the cache in one
/// transaction.
///
/// Polling rather than Graph change notifications on purpose: webhooks need a
/// publicly reachable HTTPS endpoint, which an internal kiosk on a building LAN
/// does not have. Six requests every few minutes is negligible load.
/// </summary>
public sealed class CalendarSyncWorker : BackgroundService
{
    private readonly ICalendarSource _source;
    private readonly IMeetingStore _store;
    private readonly IRoomDirectory _rooms;
    private readonly IClock _clock;
    private readonly IHubContext<StatusHub> _hub;
    private readonly SyncOptions _options;
    private readonly Zuil.Storage.StorageOptions _storage;
    private readonly ILogger<CalendarSyncWorker> _logger;

    public CalendarSyncWorker(
        ICalendarSource source,
        IMeetingStore store,
        IRoomDirectory rooms,
        IClock clock,
        IHubContext<StatusHub> hub,
        IOptions<SyncOptions> options,
        IOptions<Zuil.Storage.StorageOptions> storage,
        ILogger<CalendarSyncWorker> logger)
    {
        _source = source;
        _store = store;
        _rooms = rooms;
        _clock = clock;
        _hub = hub;
        _options = options.Value;
        _storage = storage.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't sync against a clock we don't trust yet; see SystemClock.
        while (!_clock.IsSynchronized && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Waiting for NTP synchronisation before first sync");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        using var timer = new PeriodicTimer(_options.Interval);

        do
        {
            await SyncOnceAsync(stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task SyncOnceAsync(CancellationToken ct)
    {
        var now = _clock.Now;
        var from = now - _options.WindowBehind;
        var to = now + _options.WindowAhead;

        try
        {
            var all = new List<Meeting>();

            foreach (var room in _rooms.All)
            {
                // One room failing must not cost us the other five, so a per-room
                // failure aborts the whole swap rather than caching a partial
                // building. Losing one room silently is worse than staying stale.
                var occurrences = await _source.GetOccurrencesAsync(room, from, to, ct);
                all.AddRange(occurrences);
            }

            await _store.ReplaceWindowAsync(from, to, all, ct);
            await _store.PurgeBeforeAsync(now - _storage.RetainPastMeetingsFor, ct);

            await _hub.Clients.All.SendAsync("cacheUpdated", new
            {
                syncedAt = now,
                meetingCount = all.Count,
            }, ct);

            _logger.LogInformation(
                "Synced {Count} occurrences across {Rooms} rooms", all.Count, _rooms.All.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Keep serving the previous cache. The kiosk stays useful; the status
            // endpoint and the UI banner report that data is ageing.
            _logger.LogError(ex, "Calendar sync failed; serving previous cache");
            await _store.RecordSyncAttemptAsync(now, ex.Message, CancellationToken.None);
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
