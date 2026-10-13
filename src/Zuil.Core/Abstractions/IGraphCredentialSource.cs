namespace Zuil.Core.Abstractions;

/// <summary>
/// App-only credentials for Microsoft Graph, secret included.
/// </summary>
/// <param name="TenantId">Entra tenant the app is registered in.</param>
/// <param name="ClientId">Application (client) id.</param>
/// <param name="ClientSecret">
/// Plaintext secret, decrypted. Null when the install uses a certificate
/// instead, or is not configured yet.
/// </param>
/// <param name="CertificatePath">
/// Set only from the config file, never from the GUI. When present it wins over
/// the secret, so a hardened site that already uses a certificate loses nothing
/// by upgrading.
/// </param>
public sealed record GraphCredentials(
    string TenantId,
    string ClientId,
    string? ClientSecret,
    string? CertificatePath,
    string? CertificatePassword)
{
    /// <summary>
    /// Whether there is enough here to ask for a token at all. False on a fresh
    /// install, which the sync worker treats as "wait, don't log an error every
    /// three minutes forever".
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && (!string.IsNullOrWhiteSpace(ClientSecret) || !string.IsNullOrWhiteSpace(CertificatePath));
}

/// <summary>
/// The one way to read the Graph client secret.
///
/// Kept apart from <see cref="ISettingsStore"/> so that "no admin-facing code
/// can read the secret" is a fact about the type graph rather than a convention
/// someone has to remember. Only the token provider takes this dependency.
/// </summary>
public interface IGraphCredentialSource
{
    /// <summary>
    /// Current credentials, or null when nothing is configured. Read fresh each
    /// time rather than cached: the admin panel can change these at any moment,
    /// and a token minted with a revoked secret must not outlive it.
    /// </summary>
    Task<GraphCredentials?> GetAsync(CancellationToken ct);
}
