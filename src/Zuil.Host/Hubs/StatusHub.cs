using Microsoft.AspNetCore.SignalR;

namespace Zuil.Host.Hubs;

/// <summary>
/// Pushes cache refreshes and device state to the kiosk browser, so the screen
/// updates without polling from a page that may be open for weeks at a time.
/// </summary>
public sealed class StatusHub : Hub
{
}
