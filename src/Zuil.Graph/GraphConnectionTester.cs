using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using Zuil.Core.Abstractions;
using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Graph;

/// <summary>The outcome of a connection test, fit to put on a touchscreen.</summary>
/// <param name="Ok">Whether a token was obtained and the mailbox answered.</param>
/// <param name="Message">Dutch, and free of anything that should not be read aloud in a lobby.</param>
public sealed record GraphTestResult(bool Ok, string Message);

/// <summary>
/// Checks that the stored credentials actually work, by doing the smallest real
/// thing: acquire a token, then read one event from the first room's calendar.
///
/// Tests what is *saved*, never a credential posted alongside the request. An
/// endpoint that accepted a secret it did not persist would be a second code
/// path for building an MSAL client and a second place a secret could end up in
/// a log — for the convenience of not pressing Save first.
///
/// Registered even when the kiosk runs with --fake-calendar, because the whole
/// point is to exercise the real path.
/// </summary>
public sealed class GraphConnectionTester
{
    private readonly HttpClient _http;
    private readonly GraphTokenProvider _tokens;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<GraphConnectionTester> _logger;

    public GraphConnectionTester(
        HttpClient http,
        GraphTokenProvider tokens,
        ISettingsProvider settings,
        ILogger<GraphConnectionTester> logger)
    {
        _http = http;
        _tokens = tokens;
        _settings = settings;
        _logger = logger;
    }

    public async Task<GraphTestResult> TestAsync(CancellationToken ct)
    {
        var settings = _settings.Current;

        if (settings.Rooms.Count == 0)
        {
            return new GraphTestResult(
                false, "Er zijn nog geen zalen ingesteld, dus er is niets om te testen.");
        }

        var room = settings.Rooms[0];

        try
        {
            var token = await _tokens.GetAccessTokenAsync(ct);
            return await ProbeAsync(settings.Graph, room, token, ct);
        }
        catch (GraphNotConfiguredException)
        {
            return new GraphTestResult(false, "Vul eerst de tenant, client-id en secret in.");
        }
        catch (GraphAuthenticationException ex)
        {
            // The code is genuinely useful to an administrator — AADSTS7000215
            // is "wrong secret" — and carries nothing sensitive.
            return new GraphTestResult(
                false,
                $"Inloggen bij Entra is mislukt ({ex.ErrorCode}). Controleer de tenant, "
                + "de client-id en de secret.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Graph connection test could not reach Graph");
            return new GraphTestResult(
                false, "Geen verbinding met Outlook. Controleer het netwerk van dit apparaat.");
        }
    }

    private async Task<GraphTestResult> ProbeAsync(
        GraphSettings graph, Room room, string token, CancellationToken ct)
    {
        var from = DateTimeOffset.UtcNow;
        var to = from.AddDays(1);

        var url =
            $"{graph.BaseUrl}/users/{Uri.EscapeDataString(room.Mailbox)}/calendarView" +
            $"?startDateTime={from:o}&endDateTime={to:o}" +
            "&$select=id&$top=1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            return new GraphTestResult(
                true, $"Verbinding in orde. Het postvak van {room.DisplayName} antwoordt.");
        }

        // Log the body, report only the status: a Graph error body can be long
        // and can quote the request, and this message goes on a screen.
        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning(
            "Graph connection test failed with {Status}: {Body}", (int)response.StatusCode, body);

        var reason = (int)response.StatusCode switch
        {
            401 => "Het token is geweigerd. Controleer de secret.",
            403 => "Geen toegang tot dit postvak. Controleer de Graph-rechten en de "
                   + "ApplicationAccessPolicy; zie docs/outlook-setup.md.",
            404 => $"Het postvak {room.Mailbox} bestaat niet in deze tenant.",
            429 => "Outlook vraagt om het rustiger aan te doen. Probeer het straks opnieuw.",
            _ => $"Outlook antwoordde met status {(int)response.StatusCode}.",
        };

        return new GraphTestResult(false, reason);
    }
}
