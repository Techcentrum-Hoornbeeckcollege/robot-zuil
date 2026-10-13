namespace Zuil.Storage;

/// <summary>
/// The certificate escape hatch, bound from the "Graph" section of a config file.
///
/// Deliberately not editable from the GUI, and deliberately wins over the
/// client secret in the database. A site that already follows
/// docs/outlook-setup.md and uses a certificate loses nothing by upgrading to a
/// build with an admin panel — and a certificate on disk remains the better
/// credential for an unattended machine in a public building.
/// </summary>
public sealed class GraphCredentialOptions
{
    public const string SectionName = "Graph";

    /// <summary>Path to a .pfx. When set, the stored client secret is ignored.</summary>
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }
}
