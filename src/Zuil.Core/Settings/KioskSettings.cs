using Zuil.Core.Models;

namespace Zuil.Core.Settings;

/// <summary>
/// Everything about this kiosk that an administrator can change from the GUI,
/// as one immutable snapshot.
///
/// Snapshot rather than live properties on purpose: the sync worker reads this
/// once at the top of each pass, so a setting edited halfway through can never
/// mix two room lists into a single cache swap.
///
/// As of 0.3 this is everything: there are no configuration files and no
/// environment variables. The only thing not in here is where the database
/// itself lives, which cannot be, since this is inside it — see
/// Zuil.Storage.DataDirectory. The admin credentials are in the database too,
/// in their own table, because a password hash is not a setting.
/// </summary>
public sealed record KioskSettings
{
    public GraphSettings Graph { get; init; } = new();

    public SyncSettings Sync { get; init; } = new();

    public DeviceSettings Device { get; init; } = new();

    public PrivacySettings Privacy { get; init; } = new();

    public UiSettings Ui { get; init; } = new();

    public SecuritySettings Security { get; init; } = new();

    public NetworkSettings Network { get; init; } = new();

    public BrowserSettings Browser { get; init; } = new();

    /// <summary>
    /// The rooms this column routes to. Part of the snapshot so a room edit and
    /// the settings that depend on it are swapped together.
    /// </summary>
    public IReadOnlyList<Room> Rooms { get; init; } = [];
}

/// <summary>
/// Microsoft Graph connection details, minus the credential itself.
///
/// The client secret is never a property here. It is reachable only through
/// <see cref="Abstractions.IGraphCredentialSource"/>, so admin-facing code
/// cannot read it even by accident.
/// </summary>
public sealed record GraphSettings
{
    public string TenantId { get; init; } = "";

    public string ClientId { get; init; } = "";

    public string BaseUrl { get; init; } = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// IANA id sent to Graph as <c>Prefer: outlook.timezone</c>, and used to
    /// decide where a local day starts for the agenda board.
    /// </summary>
    public string TimeZone { get; init; } = "Europe/Amsterdam";

    /// <summary>
    /// Path to a .pfx on the device. When set it wins over the client secret: a
    /// certificate is the stronger credential for an unattended machine in a
    /// public building. The file still has to be put there by hand; only the
    /// path is a setting.
    /// </summary>
    public string CertificatePath { get; init; } = "";

    /// <summary>
    /// Whether a certificate password is stored. Like the client secret, the
    /// value itself is encrypted and never read back out.
    /// </summary>
    public bool CertificatePasswordIsSet { get; init; }

    /// <summary>
    /// Whether a client secret is stored, so the admin panel can say
    /// "ingesteld" without the value ever leaving the database. Never persisted
    /// directly.
    /// </summary>
    public bool SecretIsSet { get; init; }
}

/// <summary>How often and how far the kiosk caches its calendars.</summary>
public sealed record SyncSettings
{
    /// <summary>How often to re-read every room calendar.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>How far back the cached window starts, so in-progress meetings are found.</summary>
    public TimeSpan WindowBehind { get; init; } = TimeSpan.FromHours(2);

    /// <summary>How far ahead to cache. Today plus tomorrow is plenty for wayfinding.</summary>
    public TimeSpan WindowAhead { get; init; } = TimeSpan.FromHours(36);

    /// <summary>Cache older than this makes the kiosk show a staleness warning.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a meeting is kept after it ends. Short on purpose: this is
    /// attendee data on a machine in a public building.
    /// </summary>
    public TimeSpan RetainPastMeetingsFor { get; init; } = TimeSpan.FromHours(2);
}

/// <summary>
/// i2c link to the ESP32.
///
/// <see cref="BusId"/>, <see cref="Address"/> and <see cref="AttentionPin"/> are
/// read once at startup: re-opening an I2cDevice and re-registering GPIO
/// callbacks while the kiosk is serving is a new failure mode on a wall-mounted
/// machine for near-zero benefit. The admin panel badges them as needing a
/// restart. The rest apply live.
/// </summary>
public sealed record DeviceSettings
{
    public int BusId { get; init; } = 1;

    public int Address { get; init; } = 0x42;

    /// <summary>GPIO the ESP32 pulls to signal it has something to say; null disables it.</summary>
    public int? AttentionPin { get; init; } = 17;

    public TimeSpan ResponseDelay { get; init; } = TimeSpan.FromMilliseconds(5);

    public int MaxRetries { get; init; } = 3;

    public int MaxCommandsPerSecond { get; init; } = 10;
}

/// <summary>
/// How much of a meeting the kiosk is allowed to put on a screen in a public
/// lobby. See docs/privacy.md before widening any of this.
/// </summary>
public sealed record PrivacySettings
{
    /// <summary>
    /// Defaults to <see cref="SubjectVisibility.Hidden"/>. A fresh install shows
    /// time and room only, which is not personal data — that is what makes an
    /// all-rooms agenda board defensible at all.
    /// </summary>
    public SubjectVisibility SubjectVisibility { get; init; } = SubjectVisibility.Hidden;

    /// <summary>
    /// How long without touch before the kiosk drops back to its resting view.
    /// Not a nicety: it is what wipes the last visitor's name off a public screen.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(45);
}

/// <summary>Who may see meeting subjects.</summary>
public enum SubjectVisibility
{
    /// <summary>Nobody. The field is null in every response.</summary>
    Hidden = 0,

    /// <summary>Only a logged-in administrator, for diagnosing a wrong-room report.</summary>
    AdminOnly = 1,

    /// <summary>Anyone standing in the lobby. "Exitgesprek J. Nap" is the risk here.</summary>
    Everyone = 2,
}

/// <summary>What the kiosk puts on screen, and how much of it.</summary>
public sealed record UiSettings
{
    /// <summary>What the column shows when nobody is standing at it.</summary>
    public AttractView AttractView { get; init; } = AttractView.Upcoming;

    /// <summary>How many entries the "up next" list shows.</summary>
    public int UpcomingCount { get; init; } = 25;

    /// <summary>Whether the agenda buttons appear at all, for a site that only wants search.</summary>
    public bool ShowAgendaNav { get; init; } = true;
}

/// <summary>
/// How the admin session behaves. The credentials themselves are not here;
/// they live in their own table.
/// </summary>
public sealed record SecuritySettings
{
    /// <summary>
    /// How long an admin session survives without a request. Sliding, so
    /// working through the settings does not log you out mid-edit.
    /// </summary>
    public TimeSpan SessionIdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Failed logins within <see cref="LockoutWindow"/> before login is refused.</summary>
    public int MaxFailedAttempts { get; init; } = 5;

    /// <summary>
    /// How far back failures count. The ledger is in the database rather than in
    /// memory, so pulling the plug does not reset it.
    /// </summary>
    public TimeSpan LockoutWindow { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// What the kiosk listens on.
///
/// The host is fixed at 127.0.0.1 and is not a setting. Binding this to the LAN
/// would put the admin panel — and whatever password it currently has — on the
/// building network over plain HTTP. The port is editable because it is
/// occasionally genuinely in the way.
/// </summary>
public sealed record NetworkSettings
{
    public const string Host = "127.0.0.1";

    public int Port { get; init; } = 5000;

    /// <summary>The URL Kestrel binds, and the one the kiosk browser opens.</summary>
    public string Url => $"http://{Host}:{Port}";
}

/// <summary>
/// The browser the host launches when it is not managed by systemd. On the Pi
/// zuil-kiosk.service owns Chromium, so this stays off.
/// </summary>
public sealed record BrowserSettings
{
    public bool LaunchBrowser { get; init; }

    public string BrowserPath { get; init; } = "chromium-browser";
}

/// <summary>The kiosk's resting screen.</summary>
public enum AttractView
{
    /// <summary>The "Welkom" panel. Carries no data of any kind.</summary>
    Attract = 0,

    /// <summary>The chronological list of what is on across the building.</summary>
    Upcoming = 1,

    /// <summary>One column per room.</summary>
    Board = 2,
}
