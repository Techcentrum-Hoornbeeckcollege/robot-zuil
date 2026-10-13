using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Zuil.Storage;

/// <summary>
/// Encrypts the Graph client secret before it goes in the cache database.
///
/// Why not ASP.NET Data Protection, which would be the obvious choice: on the Pi
/// deploy/zuil.service sets <c>ProtectHome=true</c>, which makes Data
/// Protection's default key home (~/.aspnet/DataProtection-Keys) unwritable. It
/// does not fail — it silently falls back to an ephemeral key ring, so the
/// secret would be undecryptable after every restart. It would also drag a web
/// package into this non-web library. AES-GCM against an explicit key file is
/// forty lines and has an obvious answer to "where is the key".
///
/// Honest about what this buys: key and ciphertext sit on the same disk under the
/// same uid. It protects the credential from a database copied off in a backup or
/// a support dump, and from the plaintext config file it replaces. It does not
/// protect against root, against the service account, or against someone walking
/// off with the SD card. Full-disk encryption, or a certificate in a TPM, is the
/// real answer — see docs/privacy.md.
/// </summary>
public sealed class AesGcmSecretProtector
{
    private const string Version = "v1";
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;   // AesGcm.NonceByteSizes.MaxSize
    private const int TagBytes = 16;     // AesGcm.TagByteSizes.MaxSize

    private readonly string _keyPath;
    private readonly ILogger<AesGcmSecretProtector> _logger;
    private readonly object _gate = new();

    private byte[]? _key;

    public AesGcmSecretProtector(string keyPath, ILogger<AesGcmSecretProtector> logger)
    {
        _keyPath = keyPath;
        _logger = logger;
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> into a self-describing payload.
    /// A fresh nonce per call, so storing the same secret twice does not produce
    /// the same ciphertext.
    /// </summary>
    public string Protect(string plaintext)
    {
        var key = GetOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag);

        // Tag appended to the ciphertext so the payload is two fields, not three.
        var envelope = new byte[cipher.Length + TagBytes];
        cipher.CopyTo(envelope, 0);
        tag.CopyTo(envelope, cipher.Length);

        return string.Join('.', Version, Convert.ToBase64String(nonce), Convert.ToBase64String(envelope));
    }

    /// <summary>
    /// Decrypts a payload produced by <see cref="Protect"/>, or returns null if
    /// it is malformed, tampered with, or was encrypted under a different key.
    ///
    /// Null rather than an exception on purpose: a replaced or lost key file then
    /// degrades to "Outlook is not configured", which an administrator fixes by
    /// re-entering the secret, instead of a boot crash loop on a wall-mounted
    /// machine.
    /// </summary>
    public string? Unprotect(string payload)
    {
        var parts = payload.Split('.');
        if (parts.Length != 3 || parts[0] != Version)
        {
            _logger.LogWarning("Stored secret is not in the expected format; treating as unset");
            return null;
        }

        byte[] nonce;
        byte[] envelope;
        try
        {
            nonce = Convert.FromBase64String(parts[1]);
            envelope = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Stored secret is not valid base64; treating as unset");
            return null;
        }

        if (nonce.Length != NonceBytes || envelope.Length < TagBytes)
        {
            _logger.LogWarning("Stored secret has the wrong length; treating as unset");
            return null;
        }

        if (!TryReadKey(out var key))
        {
            // No key file means the secret cannot be read, which is exactly what
            // deleting the key file is supposed to accomplish.
            _logger.LogWarning(
                "A secret is stored but {KeyPath} is missing; re-enter it in the admin panel",
                _keyPath);
            return null;
        }

        var cipher = envelope.AsSpan(0, envelope.Length - TagBytes);
        var tag = envelope.AsSpan(envelope.Length - TagBytes);
        var plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            // Authentication failure: wrong key, or someone edited the row.
            _logger.LogError(
                "Stored secret failed authentication. The key file may have been replaced; "
                + "re-enter the secret in the admin panel.");
            return null;
        }

        return Encoding.UTF8.GetString(plain);
    }

    private byte[] GetOrCreateKey()
    {
        lock (_gate)
        {
            if (_key is not null)
            {
                return _key;
            }

            if (TryLoadKeyFile(out var existing))
            {
                _key = existing;
                return _key;
            }

            var key = RandomNumberGenerator.GetBytes(KeyBytes);

            var directory = Path.GetDirectoryName(Path.GetFullPath(_keyPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(_keyPath, key);
            TryRestrictPermissions(_keyPath);

            _logger.LogInformation(
                "Created a new secret key at {KeyPath}. Back it up together with the database: "
                + "either one alone cannot recover the Graph credential.",
                _keyPath);

            _key = key;
            return _key;
        }
    }

    private bool TryReadKey(out byte[] key)
    {
        lock (_gate)
        {
            if (_key is not null)
            {
                key = _key;
                return true;
            }

            if (TryLoadKeyFile(out var loaded))
            {
                _key = loaded;
                key = loaded;
                return true;
            }
        }

        key = [];
        return false;
    }

    private bool TryLoadKeyFile(out byte[] key)
    {
        key = [];

        if (!File.Exists(_keyPath))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(_keyPath);
        if (bytes.Length != KeyBytes)
        {
            _logger.LogError(
                "{KeyPath} is {Actual} bytes, expected {Expected}; ignoring it",
                _keyPath, bytes.Length, KeyBytes);
            return false;
        }

        key = bytes;
        return true;
    }

    private void TryRestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Worth a loud warning but not worth refusing to start: a kiosk that
            // will not boot is worse than one with a readable key file.
            _logger.LogWarning(ex, "Could not restrict permissions on {KeyPath}", path);
        }
    }
}
