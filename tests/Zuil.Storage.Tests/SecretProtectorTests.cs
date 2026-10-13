using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Zuil.Storage.Tests;

/// <summary>
/// The Graph client secret is the one genuinely sensitive thing this kiosk
/// holds, and it now lives in a database file on a machine in a public
/// building. These tests pin what the encryption does and — just as
/// importantly — what it does when the key is gone.
/// </summary>
public class SecretProtectorTests
{
    private const string Secret = "n0t-a-real-secret~Abc.123_XYZ";

    [Fact]
    public void Round_trips()
    {
        using var db = new TestDatabase();

        var payload = db.Protector.Protect(Secret);

        Assert.Equal(Secret, db.Protector.Unprotect(payload));
    }

    [Fact]
    public void Does_not_leave_the_plaintext_in_the_payload()
    {
        using var db = new TestDatabase();

        var payload = db.Protector.Protect(Secret);

        Assert.DoesNotContain(Secret, payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fresh nonce per call, so two installs with the same secret — or one
    /// install that re-saves it — do not produce identical ciphertext.
    /// </summary>
    [Fact]
    public void Produces_different_ciphertext_each_time()
    {
        using var db = new TestDatabase();

        Assert.NotEqual(db.Protector.Protect(Secret), db.Protector.Protect(Secret));
    }

    [Fact]
    public void Returns_null_when_the_payload_was_tampered_with()
    {
        using var db = new TestDatabase();

        var payload = db.Protector.Protect(Secret);
        var parts = payload.Split('.');

        // Flip a bit in the ciphertext. AES-GCM authenticates, so this must not
        // decrypt to anything at all.
        var bytes = Convert.FromBase64String(parts[2]);
        bytes[0] ^= 0x01;
        var tampered = string.Join('.', parts[0], parts[1], Convert.ToBase64String(bytes));

        Assert.Null(db.Protector.Unprotect(tampered));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("v1.only-two-parts")]
    [InlineData("v2.AAAA.AAAA")]
    [InlineData("v1.not-base64!.AAAA")]
    public void Returns_null_for_a_malformed_payload(string payload)
    {
        using var db = new TestDatabase();

        Assert.Null(db.Protector.Unprotect(payload));
    }

    /// <summary>
    /// A replaced key must degrade to "not configured", so an administrator
    /// re-enters the secret — not to an exception on a boot path, which on a
    /// wall-mounted kiosk means a restart loop.
    /// </summary>
    [Fact]
    public void Returns_null_when_the_key_has_been_replaced()
    {
        using var db = new TestDatabase();

        var payload = db.Protector.Protect(Secret);

        var other = new AesGcmSecretProtector(
            Path.Combine(Path.GetDirectoryName(db.Path_)!, "different.key"),
            NullLogger<AesGcmSecretProtector>.Instance);

        // Force the second protector to create its own key.
        other.Protect("anything");

        Assert.Null(other.Unprotect(payload));
    }

    [Fact]
    public void Returns_null_when_the_key_file_is_missing()
    {
        using var db = new TestDatabase();

        var payload = db.Protector.Protect(Secret);
        File.Delete(db.Database.SecretKeyPath);

        var fresh = new AesGcmSecretProtector(
            db.Database.SecretKeyPath,
            NullLogger<AesGcmSecretProtector>.Instance);

        Assert.Null(fresh.Unprotect(payload));
    }

    [Fact]
    public void Writes_the_key_file_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var db = new TestDatabase();
        db.Protector.Protect(Secret);

        var mode = File.GetUnixFileMode(db.Database.SecretKeyPath);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    /// <summary>
    /// The point of all of the above: the secret must not be sitting in the
    /// database file, because that file gets copied off in backups and support
    /// dumps.
    /// </summary>
    [Fact]
    public async Task Stored_secret_never_appears_in_the_database_bytes()
    {
        using var db = new TestDatabase();

        await db.Settings.SetGraphSecretAsync(Secret, CancellationToken.None);

        var raw = db.RawBytes();

        Assert.False(Contains(raw, Encoding.UTF8.GetBytes(Secret)), "found as UTF-8");
        Assert.False(Contains(raw, Encoding.Unicode.GetBytes(Secret)), "found as UTF-16");
    }

    /// <summary>
    /// Whether <paramref name="needle"/> appears anywhere in
    /// <paramref name="haystack"/>. Written out because xunit's DoesNotContain
    /// over a collection looks for one element, not a run of them.
    /// </summary>
    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}
