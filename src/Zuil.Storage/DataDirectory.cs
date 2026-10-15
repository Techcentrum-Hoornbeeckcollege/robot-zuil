using Microsoft.Extensions.Logging;

namespace Zuil.Storage;

/// <summary>
/// Works out where the database and its key file live, with nothing to
/// configure.
///
/// This is the one path that cannot come from settings, because the settings
/// are in it. Everything else the kiosk needs is in the database and editable
/// from the GUI; this is the bootstrap, so it is decided by rules rather than
/// by a file somebody has to write.
///
/// The rules, in order:
///
///  1. A <c>zuil-data</c> directory beside the executable, if one already
///     exists. Lets a copied-anywhere exe keep its data with it, and lets a
///     developer wipe state by deleting one folder.
///  2. The platform's state directory — <c>/var/lib/zuil</c> on Linux,
///     <c>%ProgramData%\zuil</c> on Windows. Deliberately NOT next to the
///     binary: scripts/installzuil.sh stages each release in its own folder, so
///     anything beside the exe is gone on the next update.
///  3. <c>zuil-data</c> beside the executable, created. The fallback for a
///     laptop where /var/lib is not writable.
/// </summary>
public static class DataDirectory
{
    private const string FolderName = "zuil";
    private const string PortableFolderName = "zuil-data";

    /// <summary>Where a pre-0.3 install kept its database, for the one-time move.</summary>
    private const string LegacyLinuxPath = "/opt/zuil/data";

    public static string Resolve(ILogger logger)
    {
        var portable = Path.Combine(AppContext.BaseDirectory, PortableFolderName);

        // Rule 1: an existing portable directory wins, so moving an install
        // somewhere else does not silently leave its data behind.
        if (Directory.Exists(portable))
        {
            logger.LogInformation("Using the data directory beside the binary: {Path}", portable);
            return portable;
        }

        var shared = SharedPath();
        if (shared is not null && TryPrepare(shared, logger))
        {
            MigrateLegacy(shared, logger);
            return shared;
        }

        // Rule 3: /var/lib was not writable — a dev run, or an unprivileged
        // user. Keep going rather than refusing to start.
        logger.LogInformation(
            "Falling back to a data directory beside the binary: {Path}", portable);
        Directory.CreateDirectory(portable);
        return portable;
    }

    private static string? SharedPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);

            return string.IsNullOrEmpty(programData)
                ? null
                : Path.Combine(programData, FolderName);
        }

        if (OperatingSystem.IsLinux())
        {
            // systemd's StateDirectory=zuil creates and owns this for the
            // service; see deploy/zuil.service.
            return Path.Combine("/var/lib", FolderName);
        }

        // macOS and anything else: no agreed system-wide spot worth guessing at,
        // so use the portable directory.
        return null;
    }

    private static bool TryPrepare(string path, ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(path);

            // Creating it is not proof it is writable: /var/lib can exist and
            // still be read-only to this user. Find out now rather than when the
            // first write fails.
            var probe = Path.Combine(path, ".write-probe");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "{Path} is not usable as a data directory", path);
            return false;
        }
    }

    /// <summary>
    /// Moves a pre-0.3 database across, once.
    ///
    /// Earlier builds put the database under /opt/zuil/data and were pointed at
    /// it by an environment file. That file is gone, so without this an upgrade
    /// would look like a factory reset: no rooms, no credentials, and an Outlook
    /// secret still sitting in the old directory.
    /// </summary>
    private static void MigrateLegacy(string destination, ILogger logger)
    {
        if (!OperatingSystem.IsLinux()
            || string.Equals(destination, LegacyLinuxPath, StringComparison.Ordinal))
        {
            return;
        }

        var legacyDatabase = Path.Combine(LegacyLinuxPath, "zuil-cache.db");
        var newDatabase = Path.Combine(destination, "zuil-cache.db");

        // Only when there is something to move and nothing to overwrite.
        if (!File.Exists(legacyDatabase) || File.Exists(newDatabase))
        {
            return;
        }

        try
        {
            // Copy rather than move: if this build turns out to be a mistake and
            // someone rolls back, the old install still has its data.
            File.Copy(legacyDatabase, newDatabase);

            var legacyKey = Path.Combine(LegacyLinuxPath, "secret.key");
            if (File.Exists(legacyKey))
            {
                var newKey = Path.Combine(destination, "secret.key");
                File.Copy(legacyKey, newKey);
                TryRestrict(newKey);
            }

            logger.LogInformation(
                "Copied the database from {Old} to {New}. The old copy is left in place; "
                + "delete it once this release is known good — it holds a credential.",
                LegacyLinuxPath, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the kiosk starts empty and an administrator can set it
            // up again from the GUI, which is the whole point of this release.
            logger.LogWarning(
                ex, "Could not copy the old database from {Old}; starting fresh", LegacyLinuxPath);
        }
    }

    private static void TryRestrict(string path)
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
            // AesGcmSecretProtector logs loudly if it cannot restrict its own
            // key file; a copy that kept the source's mode is no worse.
        }
    }
}
