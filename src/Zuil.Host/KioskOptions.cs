namespace Zuil.Host;

public sealed class KioskOptions
{
    public const string SectionName = "Kiosk";

    /// <summary>
    /// False on the Pi, where deploy/zuil-kiosk.service runs Chromium as its own
    /// systemd unit so the browser and the backend can be restarted
    /// independently. True is for `dotnet run` on a desktop.
    /// </summary>
    public bool LaunchBrowser { get; set; }

    public string BrowserPath { get; set; } = "chromium-browser";

    public string Url { get; set; } = "http://localhost:5000";
}
