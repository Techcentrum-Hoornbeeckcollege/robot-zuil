using System.ComponentModel.DataAnnotations;

namespace Zuil.Graph;

/// <summary>
/// Bound from the "Graph" section of config/appsettings.Production.json.
/// Never commit a populated copy of this; see docs/outlook-setup.md.
/// </summary>
public sealed class GraphOptions
{
    public const string SectionName = "Graph";

    [Required]
    public string TenantId { get; set; } = "";

    [Required]
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Path to the PKCS#12 certificate used for app-only auth. Prefer this over
    /// <see cref="ClientSecret"/>: a secret in a config file next to an
    /// unattended kiosk in a public building is a credential anyone with
    /// physical access can walk off with.
    /// </summary>
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Fallback for development only. Lock the app down with an Entra
    /// ApplicationAccessPolicy regardless, so this credential can read only the
    /// six room mailboxes and nothing else in the tenant.
    /// </summary>
    public string? ClientSecret { get; set; }

    public string BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// IANA timezone requested via the Prefer header, so Graph returns local
    /// times and we are not converting by hand.
    /// </summary>
    public string TimeZone { get; set; } = "Europe/Amsterdam";
}
