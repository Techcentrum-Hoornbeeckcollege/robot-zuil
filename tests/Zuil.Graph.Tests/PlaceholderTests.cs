namespace Zuil.Graph.Tests;

/// <summary>
/// TODO: cover the calendarView mapping against recorded Graph responses —
/// specifically a recurring series (which /calendarView expands and /events does
/// not) and a DST boundary, since both produce wrong-room bugs rather than
/// crashes. Use a stubbed HttpMessageHandler; do not call a real tenant.
/// </summary>
public class PlaceholderTests
{
    [Fact(Skip = "Not implemented yet")]
    public void Expands_recurring_series_across_a_dst_boundary() => Assert.True(false);
}
