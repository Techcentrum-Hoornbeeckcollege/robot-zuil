using Zuil.Core.Admin;

namespace Zuil.Core.Tests;

/// <summary>
/// The admin password is the only thing between a lobby touchscreen and the
/// kiosk's settings, and it now lives in a database file on the device.
/// </summary>
public class PasswordHasherTests
{
    [Fact]
    public void Accepts_the_password_it_hashed() =>
        Assert.True(PasswordHasher.Verify("correct horse", PasswordHasher.Hash("correct horse")));

    [Fact]
    public void Rejects_a_different_password() =>
        Assert.False(PasswordHasher.Verify("wrong", PasswordHasher.Hash("correct horse")));

    [Fact]
    public void Is_case_sensitive() =>
        Assert.False(PasswordHasher.Verify("PASSWORD", PasswordHasher.Hash("password")));

    [Fact]
    public void Does_not_store_the_password()
    {
        var hash = PasswordHasher.Hash("correct horse");

        Assert.DoesNotContain("correct horse", hash, StringComparison.Ordinal);
    }

    /// <summary>
    /// A per-password salt, so two kiosks sharing a password do not share a
    /// hash — and so a stolen database cannot be attacked with one table.
    /// </summary>
    [Fact]
    public void Salts_every_hash()
    {
        Assert.NotEqual(PasswordHasher.Hash("password"), PasswordHasher.Hash("password"));
    }

    [Fact]
    public void Writes_its_parameters_into_the_hash()
    {
        var parts = PasswordHasher.Hash("password").Split('$');

        // Self-describing, so the iteration count can be raised later without
        // invalidating what is already stored.
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.True(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) >= 100_000);
    }

    /// <summary>
    /// A corrupted or truncated row must lock the panel, never open it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("pbkdf2-sha256$600000$onlythree")]
    [InlineData("pbkdf2-sha256$notanumber$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$0$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$-1$AAAA$AAAA")]
    [InlineData("bcrypt$600000$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$600000$not base64!$AAAA")]
    [InlineData("pbkdf2-sha256$600000$$")]
    public void Rejects_a_malformed_hash(string stored) =>
        Assert.False(PasswordHasher.Verify("password", stored));

    /// <summary>
    /// Verifying has to keep working against hashes written with a different
    /// iteration count, or raising it would lock every existing install out.
    /// </summary>
    [Fact]
    public void Verifies_a_hash_made_with_other_parameters()
    {
        // Built the way Hash does, but with a deliberately different count.
        const string password = "password";
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(password),
            salt,
            1000,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            32);

        var stored = string.Join(
            '$',
            "pbkdf2-sha256",
            "1000",
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));

        Assert.True(PasswordHasher.Verify(password, stored));
        Assert.False(PasswordHasher.Verify("something else", stored));
    }

    /// <summary>The shipped pair, which the panel warns about until changed.</summary>
    [Fact]
    public void Default_credentials_are_what_the_installer_documents()
    {
        Assert.Equal("admin", AdminCredential.DefaultUsername);
        Assert.Equal("password", AdminCredential.DefaultPassword);
    }
}
