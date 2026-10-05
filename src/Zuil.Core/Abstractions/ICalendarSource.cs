using Zuil.Core.Models;

namespace Zuil.Core.Abstractions;

/// <summary>
/// Reads meeting occurrences for a room over a time window. Implemented by
/// Zuil.Graph against Microsoft Graph, and by FakeCalendarSource for development
/// without a tenant.
/// </summary>
public interface ICalendarSource
{
    /// <summary>
    /// Returns every occurrence overlapping [from, to) for one room. Recurring
    /// series must already be expanded by the implementation.
    /// </summary>
    Task<IReadOnlyList<Meeting>> GetOccurrencesAsync(
        Room room,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
