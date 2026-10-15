using Zuil.Core.Abstractions;
using Zuil.Core.Admin;

namespace Zuil.Host.Settings;

/// <summary>
/// Makes sure there is an admin account to log in with.
///
/// Seeded rather than shipped in a file, because there are no files: a fresh
/// kiosk has to be reachable by someone standing in front of it with no SSH
/// access and nothing to edit.
/// </summary>
public static class AdminBootstrap
{
    public static async Task EnsureCredentialAsync(
        ISettingsStore store, ILogger logger, CancellationToken ct)
    {
        if (await store.GetAdminCredentialAsync(ct) is { } existing)
        {
            if (existing.IsDefault)
            {
                WarnAboutDefault(logger, existing.Username);
            }

            return;
        }

        await store.SetAdminCredentialAsync(
            new AdminCredential(
                Username: AdminCredential.DefaultUsername,
                PasswordHash: PasswordHasher.Hash(AdminCredential.DefaultPassword),
                IsDefault: true),
            ct);

        logger.LogInformation(
            "Created the admin account {Username} with the default password",
            AdminCredential.DefaultUsername);

        WarnAboutDefault(logger, AdminCredential.DefaultUsername);
    }

    /// <summary>
    /// Said on every start until it is changed, because the panel's own banner
    /// is only seen by someone who already got in.
    /// </summary>
    private static void WarnAboutDefault(ILogger logger, string username) =>
        logger.LogWarning(
            "The admin account ({Username}) still has the default password. Anyone who knows "
            + "this project can open Instellingen from the kiosk itself. Change it under "
            + "Instellingen → Account.",
            username);
}
