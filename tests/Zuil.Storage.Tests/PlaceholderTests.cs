namespace Zuil.Storage.Tests;

/// <summary>
/// TODO: cover SqliteMeetingStore against an in-memory database — the atomic
/// window swap (a reader must never see a half-rebuilt cache) and the fact that
/// FindByAttendeeAsync matches exactly and never by prefix.
/// </summary>
public class PlaceholderTests
{
    [Fact(Skip = "Not implemented yet")]
    public void Replaces_the_window_atomically() => Assert.True(false);
}
