namespace Zuil.Core.Admin;

/// <summary>
/// The one admin account.
/// </summary>
/// <param name="Username">Shown on the login form; editable like everything else.</param>
/// <param name="PasswordHash">
/// A <see cref="PasswordHasher"/> string. The plaintext is never stored and
/// never leaves the login request.
/// </param>
/// <param name="IsDefault">
/// True while the shipped admin/password pair is still in place. Drives the
/// warning banner and the startup log line. Set false the moment either is
/// changed, rather than recomputed by hashing the default on every check —
/// which would mean doing 600k PBKDF2 iterations to answer "should I nag".
/// </param>
public sealed record AdminCredential(string Username, string PasswordHash, bool IsDefault)
{
    /// <summary>The shipped username. Seeded on first run, changeable after.</summary>
    public const string DefaultUsername = "admin";

    /// <summary>
    /// The shipped password. Weak on purpose so a fresh install is reachable,
    /// and the reason the panel warns until it is changed.
    /// </summary>
    public const string DefaultPassword = "password";
}
