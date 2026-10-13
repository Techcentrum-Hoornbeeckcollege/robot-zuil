using System.Security.Cryptography;
using System.Text;
using Zuil.Core.Admin;

namespace Zuil.Host.Admin;

/// <summary>
/// How an admin session is carried: an opaque random token in a cookie, with
/// only its hash held server-side.
///
/// Not cookie authentication, which would need a Data Protection key ring — and
/// on the Pi systemd sets ProtectHome=true, making Data Protection's default key
/// home unwritable, so it would silently fall back to ephemeral keys and drop
/// every session on restart anyway. This gets the same property with nothing to
/// misconfigure.
/// </summary>
public static class AdminSession
{
    public const string CookieName = "zuil_admin";

    /// <summary>
    /// A fresh 256-bit token. Returned to the browser once and never stored, so
    /// the database and a memory dump both hold only the hash.
    /// </summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>
    /// No Secure flag: Kestrel serves plaintext on loopback, and marking the
    /// cookie Secure would stop the kiosk's own browser sending it at all.
    /// Path is scoped to the admin API so it is not attached to every request
    /// for a static asset.
    /// </summary>
    public static CookieOptions CookieOptions(bool deleting = false) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = false,
        Path = "/api/admin",
        Expires = deleting ? DateTimeOffset.UnixEpoch : null,
    };

    /// <summary>The session token on this request, if there is one.</summary>
    public static string? TokenFrom(HttpRequest request) =>
        request.Cookies.TryGetValue(CookieName, out var token) && !string.IsNullOrEmpty(token)
            ? token
            : null;
}

/// <summary>
/// Rejects admin requests without a live session.
///
/// An endpoint filter attached visibly to the route group rather than
/// middleware: middleware ordering is the kind of thing that is wrong for six
/// months without anyone noticing, whereas a filter on the group is right there
/// in the route table.
/// </summary>
public sealed class AdminSessionFilter : IEndpointFilter
{
    private readonly AdminSessionStore _sessions;

    public AdminSessionFilter(AdminSessionStore sessions) => _sessions = sessions;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var token = AdminSession.TokenFrom(context.HttpContext.Request);

        if (token is null || !_sessions.Validate(AdminSession.Hash(token)))
        {
            // Deliberately bare: nothing about why, and no hint whether the
            // cookie was absent, unknown or expired.
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
