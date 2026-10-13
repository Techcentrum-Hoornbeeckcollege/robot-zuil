using System.Security.Cryptography;
using System.Text;

namespace Zuil.Core.Admin;

/// <summary>
/// Hashes the admin password for storage.
///
/// PBKDF2-HMAC-SHA256 with a per-password salt, written as a self-describing
/// string so the iteration count can be raised later without invalidating
/// what is already stored:
///
/// <code>pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</code>
///
/// Everything here is BCL — no package, which matters because Zuil.Core is the
/// project that must stay dependency-free.
///
/// This protects the password if the database is copied off the device. It does
/// nothing about the password being weak, which the default very much is: see
/// docs/privacy.md.
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>
    /// OWASP's floor for PBKDF2-HMAC-SHA256 at the time of writing. A Pi 5 does
    /// this in well under a tenth of a second, and it runs once per login.
    /// </summary>
    private const int DefaultIterations = 600_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, DefaultIterations);

        return string.Join(
            '$',
            Prefix,
            DefaultIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>
    /// Whether <paramref name="password"/> matches <paramref name="stored"/>.
    /// False for anything malformed, so a corrupted row locks the panel rather
    /// than opening it.
    /// </summary>
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');

        if (parts.Length != 4
            || parts[0] != Prefix
            || !int.TryParse(
                parts[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Derive(password, salt, iterations, expected.Length);

        // Constant-time, so the endpoint does not leak the hash one byte at a
        // time through its response time.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            length);
}
