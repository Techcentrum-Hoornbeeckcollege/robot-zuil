using Zuil.Core.Admin;
using Zuil.Core.Settings;

namespace Zuil.Storage.Tests;

/// <summary>
/// The admin account and the settings that used to live in a config file.
/// After 0.3 there are no config files, so everything here is the only copy.
/// </summary>
public class AdminCredentialStoreTests
{
    [Fact]
    public async Task Starts_with_no_account()
    {
        using var db = new TestDatabase();

        Assert.Null(await db.Settings.GetAdminCredentialAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Round_trips_an_account()
    {
        using var db = new TestDatabase();

        var hash = PasswordHasher.Hash("hunter2");
        await db.Settings.SetAdminCredentialAsync(
            new AdminCredential("beheerder", hash, IsDefault: false), CancellationToken.None);

        var loaded = await db.Settings.GetAdminCredentialAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("beheerder", loaded.Username);
        Assert.False(loaded.IsDefault);
        Assert.True(PasswordHasher.Verify("hunter2", loaded.PasswordHash));
    }

    [Fact]
    public async Task Replaces_rather_than_adds()
    {
        using var db = new TestDatabase();

        await db.Settings.SetAdminCredentialAsync(
            new AdminCredential("admin", PasswordHasher.Hash("one"), IsDefault: true),
            CancellationToken.None);
        await db.Settings.SetAdminCredentialAsync(
            new AdminCredential("other", PasswordHasher.Hash("two"), IsDefault: false),
            CancellationToken.None);

        var loaded = await db.Settings.GetAdminCredentialAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("other", loaded.Username);
        Assert.False(PasswordHasher.Verify("one", loaded.PasswordHash));
    }

    /// <summary>
    /// The password must not be sitting in the database in the clear, because
    /// that file gets copied off in backups and support dumps.
    /// </summary>
    [Fact]
    public async Task Plaintext_password_never_reaches_the_database()
    {
        using var db = new TestDatabase();
        const string password = "a-very-distinctive-password";

        await db.Settings.SetAdminCredentialAsync(
            new AdminCredential("admin", PasswordHasher.Hash(password), IsDefault: false),
            CancellationToken.None);

        var raw = db.RawBytes();
        var needle = System.Text.Encoding.UTF8.GetBytes(password);

        Assert.False(Contains(raw, needle), "the password was found in the database file");
    }

    [Fact]
    public async Task Round_trips_the_settings_that_left_the_config_file()
    {
        using var db = new TestDatabase();

        var saved = new KioskSettings
        {
            Security = new SecuritySettings
            {
                SessionIdleTimeout = TimeSpan.FromMinutes(42),
                MaxFailedAttempts = 9,
                LockoutWindow = TimeSpan.FromMinutes(11),
            },
            Network = new NetworkSettings { Port = 8080 },
            Browser = new BrowserSettings
            {
                LaunchBrowser = true,
                BrowserPath = "/usr/bin/chromium",
            },
        };

        await db.Settings.SaveAsync(saved, CancellationToken.None);
        var loaded = await db.Settings.LoadAsync(CancellationToken.None);

        Assert.Equal(saved.Security, loaded.Security);
        Assert.Equal(saved.Network, loaded.Network);
        Assert.Equal(saved.Browser, loaded.Browser);
    }

    /// <summary>The host is a constant, so a round trip cannot move it.</summary>
    [Fact]
    public async Task Listens_on_loopback_whatever_the_port()
    {
        using var db = new TestDatabase();

        await db.Settings.SaveAsync(
            new KioskSettings { Network = new NetworkSettings { Port = 9999 } },
            CancellationToken.None);

        var loaded = await db.Settings.LoadAsync(CancellationToken.None);

        Assert.Equal("http://127.0.0.1:9999", loaded.Network.Url);
    }

    [Fact]
    public async Task Round_trips_the_certificate_password_encrypted()
    {
        using var db = new TestDatabase();
        const string password = "pfx-password-here";

        await db.Settings.SetCertificatePasswordAsync(password, CancellationToken.None);
        await db.Settings.SaveAsync(
            new KioskSettings
            {
                Graph = new GraphSettings
                {
                    TenantId = "11111111-1111-1111-1111-111111111111",
                    ClientId = "22222222-2222-2222-2222-222222222222",
                    CertificatePath = "/var/lib/zuil/zuil-graph.pfx",
                },
            },
            CancellationToken.None);

        var credentials = await db.Settings.GetAsync(CancellationToken.None);

        Assert.NotNull(credentials);
        Assert.Equal(password, credentials.CertificatePassword);
        Assert.Equal("/var/lib/zuil/zuil-graph.pfx", credentials.CertificatePath);

        // A certificate alone is enough; no client secret needed.
        Assert.True(credentials.IsComplete);

        var raw = db.RawBytes();
        Assert.False(
            Contains(raw, System.Text.Encoding.UTF8.GetBytes(password)),
            "the certificate password was found in the database file");
    }

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
