using System.Text.Json.Serialization;

namespace Zuil.Graph.Dto;

/// <summary>Shape of the /calendarView response, trimmed to what we $select.</summary>
internal sealed class CalendarViewResponse
{
    [JsonPropertyName("value")]
    public List<EventDto> Value { get; set; } = [];

    /// <summary>Graph pages at 10 events by default; follow this until null.</summary>
    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}

internal sealed class EventDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("isCancelled")]
    public bool IsCancelled { get; set; }

    [JsonPropertyName("start")]
    public DateTimeTimeZoneDto? Start { get; set; }

    [JsonPropertyName("end")]
    public DateTimeTimeZoneDto? End { get; set; }

    [JsonPropertyName("attendees")]
    public List<AttendeeDto> Attendees { get; set; } = [];
}

internal sealed class DateTimeTimeZoneDto
{
    /// <summary>ISO-8601 without an offset, in <see cref="TimeZone"/>.</summary>
    [JsonPropertyName("dateTime")]
    public string DateTime { get; set; } = "";

    [JsonPropertyName("timeZone")]
    public string TimeZone { get; set; } = "UTC";
}

internal sealed class AttendeeDto
{
    [JsonPropertyName("emailAddress")]
    public EmailAddressDto? EmailAddress { get; set; }
}

internal sealed class EmailAddressDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }
}
