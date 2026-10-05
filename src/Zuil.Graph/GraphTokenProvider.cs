using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using System.Security.Cryptography.X509Certificates;

namespace Zuil.Graph;

/// <summary>
/// Acquires app-only (client credentials) tokens for Graph.
///
/// App-only rather than delegated because nobody signs in to a kiosk. MSAL keeps
/// an in-memory token cache and handles expiry, so callers just ask every time.
/// </summary>
public sealed class GraphTokenProvider
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    private readonly IConfidentialClientApplication _app;
    private readonly ILogger<GraphTokenProvider> _logger;

    public GraphTokenProvider(IOptions<GraphOptions> options, ILogger<GraphTokenProvider> logger)
    {
        _logger = logger;
        var o = options.Value;

        var builder = ConfidentialClientApplicationBuilder
            .Create(o.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, o.TenantId);

        if (!string.IsNullOrWhiteSpace(o.CertificatePath))
        {
            // TODO(.NET 9): X509CertificateLoader.LoadPkcs12FromFile is the
            // non-obsolete replacement for the X509Certificate2 file ctor.
            var cert = X509CertificateLoader.LoadPkcs12FromFile(
                o.CertificatePath,
                o.CertificatePassword);
            builder = builder.WithCertificate(cert, sendX5C: true);
        }
        else if (!string.IsNullOrWhiteSpace(o.ClientSecret))
        {
            _logger.LogWarning(
                "Graph is authenticating with a client secret. Use a certificate in production.");
            builder = builder.WithClientSecret(o.ClientSecret);
        }
        else
        {
            throw new InvalidOperationException(
                "Graph:CertificatePath or Graph:ClientSecret must be configured.");
        }

        _app = builder.Build();
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var result = await _app.AcquireTokenForClient(Scopes).ExecuteAsync(ct);
        return result.AccessToken;
    }
}
