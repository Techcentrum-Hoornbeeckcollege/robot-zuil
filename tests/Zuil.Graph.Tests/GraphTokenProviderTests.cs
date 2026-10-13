using Microsoft.Extensions.Logging.Abstractions;
using Zuil.Core.Abstractions;

namespace Zuil.Graph.Tests;

internal sealed class StubCredentials : IGraphCredentialSource
{
    public GraphCredentials? Credentials { get; set; }

    public Task<GraphCredentials?> GetAsync(CancellationToken ct) =>
        Task.FromResult(Credentials);
}

public class GraphTokenProviderTests
{
    private static GraphCredentials Complete(
        string tenant = "11111111-1111-1111-1111-111111111111",
        string client = "22222222-2222-2222-2222-222222222222",
        string? secret = "the-secret",
        string? certPath = null,
        string? certPassword = null) =>
        new(tenant, client, secret, certPath, certPassword);

    // --- Fingerprint --------------------------------------------------------

    /// <summary>
    /// Every field that feeds the MSAL client has to be in the fingerprint. Miss
    /// one and changing it in the admin panel leaves the old client — and its
    /// token cache — quietly in place, so a revoked secret keeps working.
    /// </summary>
    [Fact]
    public void Fingerprint_changes_when_the_tenant_changes() =>
        Assert.NotEqual(
            GraphTokenProvider.Fingerprint(Complete()),
            GraphTokenProvider.Fingerprint(Complete(tenant: "33333333-3333-3333-3333-333333333333")));

    [Fact]
    public void Fingerprint_changes_when_the_client_changes() =>
        Assert.NotEqual(
            GraphTokenProvider.Fingerprint(Complete()),
            GraphTokenProvider.Fingerprint(Complete(client: "44444444-4444-4444-4444-444444444444")));

    [Fact]
    public void Fingerprint_changes_when_the_secret_changes() =>
        Assert.NotEqual(
            GraphTokenProvider.Fingerprint(Complete()),
            GraphTokenProvider.Fingerprint(Complete(secret: "a-different-secret")));

    [Fact]
    public void Fingerprint_changes_when_the_certificate_changes() =>
        Assert.NotEqual(
            GraphTokenProvider.Fingerprint(Complete(certPath: "/opt/zuil/config/a.pfx")),
            GraphTokenProvider.Fingerprint(Complete(certPath: "/opt/zuil/config/b.pfx")));

    [Fact]
    public void Fingerprint_changes_when_the_certificate_password_changes() =>
        Assert.NotEqual(
            GraphTokenProvider.Fingerprint(Complete(certPath: "/a.pfx", certPassword: "one")),
            GraphTokenProvider.Fingerprint(Complete(certPath: "/a.pfx", certPassword: "two")));

    [Fact]
    public void Fingerprint_is_stable_for_the_same_credentials() =>
        Assert.Equal(
            GraphTokenProvider.Fingerprint(Complete()),
            GraphTokenProvider.Fingerprint(Complete()));

    /// <summary>
    /// Joining the fields with a separator that cannot occur in them means two
    /// different credential sets cannot collide by shifting a character across
    /// a boundary.
    /// </summary>
    [Fact]
    public void Fingerprint_does_not_collide_across_field_boundaries() =>
        Assert.NotEqual(
            GraphTokenProvider.Fingerprint(Complete(tenant: "ab", client: "c")),
            GraphTokenProvider.Fingerprint(Complete(tenant: "a", client: "bc")));

    [Fact]
    public void Fingerprint_does_not_contain_the_secret()
    {
        var fingerprint = GraphTokenProvider.Fingerprint(Complete(secret: "super-secret-value"));

        Assert.DoesNotContain("super-secret-value", fingerprint, StringComparison.Ordinal);
    }

    // --- Unconfigured -------------------------------------------------------

    /// <summary>
    /// A fresh install has no credentials. That has to be its own exception, not
    /// a generic failure, because the sync worker waits quietly on it instead of
    /// logging an error every three minutes for the life of the kiosk.
    /// </summary>
    [Fact]
    public async Task Throws_not_configured_when_nothing_is_stored()
    {
        var provider = new GraphTokenProvider(
            new StubCredentials { Credentials = null },
            NullLogger<GraphTokenProvider>.Instance);

        await Assert.ThrowsAsync<GraphNotConfiguredException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Invalidate_is_safe_before_anything_has_been_built()
    {
        var provider = new GraphTokenProvider(
            new StubCredentials { Credentials = null },
            NullLogger<GraphTokenProvider>.Instance);

        provider.Invalidate();

        await Assert.ThrowsAsync<GraphNotConfiguredException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));
    }

    // --- IsComplete ---------------------------------------------------------

    [Fact]
    public void Credentials_need_a_tenant_a_client_and_one_of_secret_or_certificate()
    {
        Assert.True(Complete().IsComplete);
        Assert.True(Complete(secret: null, certPath: "/a.pfx").IsComplete);

        Assert.False(Complete(secret: null).IsComplete);
        Assert.False(Complete(tenant: "").IsComplete);
        Assert.False(Complete(client: "").IsComplete);
        Assert.False(Complete(tenant: "   ").IsComplete);
    }
}
