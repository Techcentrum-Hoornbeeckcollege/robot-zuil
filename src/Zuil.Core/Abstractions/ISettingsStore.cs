using Zuil.Core.Admin;
using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Core.Abstractions;

/// <summary>
/// Persistence for everything the admin panel edits.
///
/// Note what is missing: there is no way to read the Graph client secret through
/// this interface. Reading it is a separate capability on
/// <see cref="IGraphCredentialSource"/>, which only the token provider depends
/// on — so admin-facing code cannot return the secret in a response even by
/// mistake. That split is the whole point of having two interfaces over one
/// table.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// The current settings, with defaults filled in for anything unset. Always
    /// succeeds on a partially-populated table: a fresh install has every key
    /// missing and must still boot.
    /// </summary>
    Task<KioskSettings> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Persists everything except the rooms and the secret. Validate first —
    /// this writes what it is given.
    /// </summary>
    Task SaveAsync(KioskSettings settings, CancellationToken ct);

    /// <summary>
    /// Replaces the whole room set in one transaction. Whole-set rather than
    /// per-room because the uniqueness rules are set-level properties.
    /// </summary>
    Task SaveRoomsAsync(IReadOnlyList<Room> rooms, CancellationToken ct);

    /// <summary>
    /// Stores the Graph client secret encrypted, or clears it when passed null.
    /// Write-only by design: there is no matching getter here.
    /// </summary>
    Task SetGraphSecretAsync(string? secret, CancellationToken ct);

    /// <summary>
    /// Whether a secret is stored, so the admin panel can show "ingesteld"
    /// without the value being read.
    /// </summary>
    Task<bool> HasGraphSecretAsync(CancellationToken ct);

    /// <summary>
    /// Stores the certificate password encrypted, or clears it when passed
    /// null. Write-only for the same reason as the client secret.
    /// </summary>
    Task SetCertificatePasswordAsync(string? password, CancellationToken ct);

    /// <summary>
    /// True once the first-run seed has happened. Checked so that an
    /// administrator who deletes every room does not have them resurrected from
    /// config/rooms.json by the next restart.
    /// </summary>
    Task<bool> IsSeededAsync(CancellationToken ct);

    /// <summary>Marks the first-run seed as done.</summary>
    Task MarkSeededAsync(CancellationToken ct);

    /// <summary>
    /// The admin account, or null before the first run has seeded one.
    /// </summary>
    Task<AdminCredential?> GetAdminCredentialAsync(CancellationToken ct);

    /// <summary>
    /// Replaces the admin account. Takes an already-hashed password: this
    /// interface has no business seeing a plaintext one.
    /// </summary>
    Task SetAdminCredentialAsync(AdminCredential credential, CancellationToken ct);

    /// <summary>
    /// Records a login attempt, for a lockout that survives a power cycle — the
    /// obvious attack on a kiosk with a known default password.
    /// </summary>
    Task RecordLoginAttemptAsync(DateTimeOffset at, bool succeeded, CancellationToken ct);

    /// <summary>Failed attempts since <paramref name="since"/>, for the lockout check.</summary>
    Task<int> CountFailedLoginsAsync(DateTimeOffset since, CancellationToken ct);

    /// <summary>Clears the failure ledger after a successful login.</summary>
    Task ClearLoginAttemptsAsync(CancellationToken ct);
}
