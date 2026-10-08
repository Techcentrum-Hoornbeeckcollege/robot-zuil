namespace Zuil.Host;

/// <summary>
/// The admin password and session rules, from configuration rather than the
/// database.
///
/// Deliberately not editable from the GUI: a password a visitor could change is
/// not a password. Set it with <c>ZUIL_Admin__Password</c>, or in
/// /etc/zuil/zuil.env which deploy/install.sh generates.
///
/// What the password protects, and what it does not: Kestrel binds 127.0.0.1
/// plaintext, so there is no network attacker and the session cookie needs no
/// Secure flag. What it keeps out is a visitor poking at the SPA and a member of
/// staff tapping into settings by accident. What it does not keep out is anyone
/// with SSH, the service account, or a USB keyboard at the column — the kiosk's
/// own Chromium can reach /api/admin, and the touchscreen is the keyboard. With
/// the default password, anyone who has read this repository can open the
/// settings panel from the lobby. Change it on install.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// The one shared password. The default is a placeholder, and
    /// <see cref="IsDefault"/> drives a warning in the UI and the log until it
    /// is changed.
    /// </summary>
    public string Password { get; set; } = DefaultPassword;

    /// <summary>
    /// How long an admin session survives without a request. Sliding, so
    /// working through the settings does not log you out mid-edit.
    /// </summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Failed attempts within <see cref="LockoutWindow"/> before login is refused.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// How far back failures count. The ledger is in the database rather than in
    /// memory, because an in-memory counter is defeated by pulling the plug —
    /// the obvious attack on a kiosk whose password might still be the default.
    /// </summary>
    public TimeSpan LockoutWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The shipped placeholder, documented in appsettings.json.</summary>
    public const string DefaultPassword = "12345678";

    public bool IsDefault =>
        string.Equals(Password, DefaultPassword, StringComparison.Ordinal);
}
