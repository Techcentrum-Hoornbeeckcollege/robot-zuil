using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Zuil.Core.Abstractions;

namespace Zuil.Graph;

/// <summary>
/// Thrown when nobody has entered Graph credentials yet.
///
/// Distinct from a failure on purpose: a fresh install has no credentials, and
/// the sync worker treats that as "wait quietly for the admin panel" rather than
/// logging an error every three minutes for the rest of the kiosk's life.
/// </summary>
public sealed class GraphNotConfiguredException : Exception
{
    public GraphNotConfiguredException()
        : base("Outlook is not configured yet. Enter the credentials under Instellingen → Outlook.")
    {
    }
}

/// <summary>
/// Thrown when Entra refused the credentials we have.
/// </summary>
/// <remarks>
/// Carries the MSAL error code and nothing else. /api/health reports the last
/// sync error to an unauthenticated caller, and raw MSAL and Graph bodies are
/// verbose enough to be worth keeping out of it. The full exception goes to the
/// log.
/// </remarks>
public sealed class GraphAuthenticationException : Exception
{
    public GraphAuthenticationException(string errorCode)
        : base($"Outlook refused the credentials ({errorCode}).") => ErrorCode = errorCode;

    public string ErrorCode { get; }
}

/// <summary>
/// Acquires app-only (client credentials) tokens for Graph.
///
/// App-only rather than delegated because nobody signs in to a kiosk.
///
/// The MSAL client used to be built once in the constructor, which was fine when
/// credentials came from a config file read at startup. Now they come from the
/// admin panel and can change while the kiosk is running, so the client is built
/// lazily and keyed by a fingerprint of the credentials that produced it. When
/// the fingerprint changes the old client is dropped — along with its in-memory
/// token cache, which is the point: a token minted with a secret that has since
/// been revoked must not outlive the revocation.
/// </summary>
public sealed class GraphTokenProvider
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    private readonly IGraphCredentialSource _credentials;
    private readonly ILogger<GraphTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _fingerprint;
    private IConfidentialClientApplication? _app;

    public GraphTokenProvider(
        IGraphCredentialSource credentials,
        ILogger<GraphTokenProvider> logger)
    {
        _credentials = credentials;
        _logger = logger;
    }

    /// <exception cref="GraphNotConfiguredException">No credentials stored yet.</exception>
    /// <exception cref="GraphAuthenticationException">Entra refused them.</exception>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var app = await GetApplicationAsync(ct);

        try
        {
            var result = await app.AcquireTokenForClient(Scopes).ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalServiceException ex)
        {
            // Log everything, surface only the code.
            _logger.LogError(ex, "Entra refused the Graph credentials");
            throw new GraphAuthenticationException(ex.ErrorCode ?? "unknown");
        }
    }

    /// <summary>
    /// Drops the cached client and its tokens.
    ///
    /// Called from the admin save path so that "test connection" immediately
    /// after saving cannot pass on a token minted with the previous secret.
    /// </summary>
    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _fingerprint = null;
            _app = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IConfidentialClientApplication> GetApplicationAsync(CancellationToken ct)
    {
        var credentials = await _credentials.GetAsync(ct)
            ?? throw new GraphNotConfiguredException();

        var fingerprint = Fingerprint(credentials);

        await _gate.WaitAsync(ct);
        try
        {
            if (_app is not null && _fingerprint == fingerprint)
            {
                return _app;
            }

            _app = Build(credentials);
            _fingerprint = fingerprint;
            return _app;
        }
        finally
        {
            _gate.Release();
        }
    }

    private IConfidentialClientApplication Build(GraphCredentials credentials)
    {
        var builder = ConfidentialClientApplicationBuilder
            .Create(credentials.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, credentials.TenantId);

        if (!string.IsNullOrWhiteSpace(credentials.CertificatePath))
        {
            var cert = X509CertificateLoader.LoadPkcs12FromFile(
                credentials.CertificatePath,
                credentials.CertificatePassword);
            builder = builder.WithCertificate(cert, sendX5C: true);

            _logger.LogInformation("Graph is authenticating with a certificate");
        }
        else if (!string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            // Not an error — it is what the admin panel configures — but worth
            // saying out loud, because a secret on an unattended machine in a
            // public lobby is weaker than a certificate. See docs/privacy.md.
            _logger.LogWarning(
                "Graph is authenticating with a client secret. A certificate is stronger for "
                + "an unattended kiosk; see docs/outlook-setup.md.");
            builder = builder.WithClientSecret(credentials.ClientSecret);
        }
        else
        {
            // IsComplete should have caught this, so reaching here is a bug.
            throw new GraphNotConfiguredException();
        }

        return builder.Build();
    }

    /// <summary>
    /// A stable digest of everything that affects which client we build.
    ///
    /// Every field that feeds <see cref="Build"/> has to be in here: miss one and
    /// changing it in the admin panel would leave the old client — and its token
    /// cache — quietly in place.
    /// </summary>
    internal static string Fingerprint(GraphCredentials credentials)
    {
        // \u001f as the separator so a value containing the separator cannot
        // make two different credential sets hash the same.
        var material = string.Join(
            '\u001f',
            credentials.TenantId,
            credentials.ClientId,
            credentials.ClientSecret ?? "",
            credentials.CertificatePath ?? "",
            credentials.CertificatePassword ?? "");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
    }
}
