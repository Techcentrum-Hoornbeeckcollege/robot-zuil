using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Zuil.Core.Abstractions;
using Zuil.Core.Models;
using Zuil.Core.Text;
using Zuil.Graph.Dto;

namespace Zuil.Graph;

/// <summary>
/// Reads room calendars from Graph.
///
/// Uses /calendarView rather than /events on purpose: calendarView expands a
/// recurring series into concrete occurrences within the window, whereas /events
/// returns the series master plus a recurrence rule you would have to expand
/// yourself (including exceptions and cancellations) — a notorious source of
/// wrong-room bugs.
/// </summary>
public sealed class CalendarViewClient : ICalendarSource
{
    private readonly HttpClient _http;
    private readonly GraphTokenProvider _tokens;
    private readonly GraphOptions _options;
    private readonly ILogger<CalendarViewClient> _logger;

    public CalendarViewClient(
        HttpClient http,
        GraphTokenProvider tokens,
        IOptions<GraphOptions> options,
        ILogger<CalendarViewClient> logger)
    {
        _http = http;
        _tokens = tokens;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Meeting>> GetOccurrencesAsync(
        Room room,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var token = await _tokens.GetAccessTokenAsync(ct);

        // $select keeps the payload — and the amount of personal data we pull
        // over the wire at all — to the minimum the kiosk renders.
        var url =
            $"{_options.BaseUrl}/users/{Uri.EscapeDataString(room.Mailbox)}/calendarView" +
            $"?startDateTime={from:o}&endDateTime={to:o}" +
            "&$select=id,subject,start,end,isCancelled,attendees" +
            "&$orderby=start/dateTime" +
            "&$top=100";

        var results = new List<Meeting>();
        var tz = ResolveTimeZone(_options.TimeZone);

        while (url is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Ask Graph to return times in our timezone instead of UTC.
            request.Headers.TryAddWithoutValidation(
                "Prefer", $"outlook.timezone=\"{_options.TimeZone}\"");

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new GraphRequestException(
                    $"calendarView for {room.Mailbox} failed with {(int)response.StatusCode}: {body}");
            }

            var page = await response.Content.ReadFromJsonAsync(
                GraphJsonContext.Default.CalendarViewResponse, ct);

            if (page is null)
            {
                break;
            }

            foreach (var dto in page.Value)
            {
                var meeting = Map(dto, room, tz);
                if (meeting is not null)
                {
                    results.Add(meeting);
                }
            }

            url = page.NextLink;
        }

        _logger.LogDebug("Fetched {Count} occurrences for {Room}", results.Count, room.Id);
        return results;
    }

    private static Meeting? Map(EventDto dto, Room room, TimeZoneInfo tz)
    {
        if (dto.Start is null || dto.End is null)
        {
            return null;
        }

        return new Meeting
        {
            Id = dto.Id,
            RoomId = room.Id,
            Subject = dto.Subject,
            Start = ToOffset(dto.Start, tz),
            End = ToOffset(dto.End, tz),
            IsCancelled = dto.IsCancelled,
            Attendees = dto.Attendees
                .Select(a => a.EmailAddress?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => new Attendee
                {
                    Name = n!,
                    NormalizedName = NameNormalizer.Normalize(n!),
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Graph returns a naive local datetime plus a timezone name; attach the real
    /// offset for that instant so DST transitions land correctly.
    /// </summary>
    private static DateTimeOffset ToOffset(DateTimeTimeZoneDto value, TimeZoneInfo fallback)
    {
        var naive = DateTime.Parse(
            value.DateTime,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None);

        var tz = string.Equals(value.TimeZone, "UTC", StringComparison.OrdinalIgnoreCase)
            ? TimeZoneInfo.Utc
            : ResolveTimeZone(value.TimeZone, fallback);

        return new DateTimeOffset(
            DateTime.SpecifyKind(naive, DateTimeKind.Unspecified),
            tz.GetUtcOffset(naive));
    }

    private static TimeZoneInfo ResolveTimeZone(string id, TimeZoneInfo? fallback = null)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return fallback ?? TimeZoneInfo.Utc;
        }
    }
}

public sealed class GraphRequestException : Exception
{
    public GraphRequestException(string message) : base(message)
    {
    }
}
