using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;

namespace Zuil.Host.Endpoints;

/// <summary>
/// What answers a request that no endpoint and no embedded file claimed.
///
/// There is one deliverable and one port, so this one handler covers three
/// different mistakes that used to look identical from the outside:
///
///  * <c>/health</c> instead of <c>/api/health</c>. The SPA fallback used to
///    swallow it — index.html with a 200, or, on a backend with no frontend
///    compiled in, a bare 500 whose text was about the frontend and so looked
///    like a different fault entirely. Both readings cost real time. The path
///    is now answered with a 404 that names the endpoint that does exist.
///  * Any other unknown <c>/api</c> or <c>/hub</c> path, which is a caller's
///    mistake and never a page: a visitor's browser does not ask for those.
///  * A backend published with an empty wwwroot, which is a build fault rather
///    than a request fault, so it answers with how to fix the build.
///
/// Anything else is a page, and the kiosk's client-side views need index.html
/// back whatever path a refresh landed on.
/// </summary>
public static class FallbackEndpoints
{
    public static void MapFrontendFallback(this IEndpointRouteBuilder app, IFileProvider frontend)
    {
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            var endpoints = context.RequestServices.GetRequiredService<EndpointDataSource>();

            if (IsBackendPath(path))
            {
                await WriteUnknownEndpointAsync(context, endpoints, suggestion: null);
                return;
            }

            // "/health" when "/api/health" is what exists. Safe to answer rather
            // than serve the SPA: the kiosk deliberately has no router, so no
            // legitimate view lives under a path of its own.
            if (ApiEquivalentOf(endpoints, path) is { } suggestion)
            {
                await WriteUnknownEndpointAsync(context, endpoints, suggestion);
                return;
            }

            var file = frontend.GetFileInfo("index.html");

            if (!file.Exists)
            {
                await WriteFrontendMissingAsync(context);
                return;
            }

            context.Response.ContentType = "text/html";
            context.Response.Headers.CacheControl = "no-cache, no-store";
            await context.Response.SendFileAsync(file);
        });
    }

    private static bool IsBackendPath(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/hub");

    /// <summary>
    /// The real route that this path would have been, with the prefix it is
    /// missing — or null if no such route exists.
    /// </summary>
    private static string? ApiEquivalentOf(EndpointDataSource endpoints, PathString path)
    {
        if (!path.HasValue || path == "/")
        {
            return null;
        }

        var candidate = "/api" + path.Value!.TrimEnd('/');

        return Routes(endpoints).FirstOrDefault(
            route => string.Equals(route, candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every routed path this process serves, which is the list worth printing
    /// to whoever just guessed one wrong. Read from the endpoint table rather
    /// than written out here, so it cannot drift from what is actually mapped.
    /// </summary>
    private static IEnumerable<string> Routes(EndpointDataSource endpoints) =>
        endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText?.TrimStart('/'))
            .Where(route => route.StartsWith("/api/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

    private static async Task WriteUnknownEndpointAsync(
        HttpContext context, EndpointDataSource endpoints, string? suggestion)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;

        await context.Response.WriteAsJsonAsync(new
        {
            error = "No such endpoint",
            path = context.Request.Path.Value,
            didYouMean = suggestion,
            endpoints = Routes(endpoints),
        });
    }

    /// <summary>
    /// Not a 404: the route matched and the server is at fault. Says what to run,
    /// because the person reading it is a developer or a maintainer on the Pi and
    /// the answer is always one of the same two commands.
    /// </summary>
    private static async Task WriteFrontendMissingAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var requested = context.Request.GetEncodedPathAndQuery();

        // Browsers get the same words with tags around them. curl and the
        // kiosk's own health check get plain text, which is what makes this
        // readable over SSH.
        if (context.Request.Headers.Accept.Any(
                accept => accept?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true))
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync($"""
                <!doctype html>
                <html lang="en">
                <head><meta charset="utf-8"><title>Frontend not built</title></head>
                <body style="font: 15px/1.6 system-ui, sans-serif; max-width: 46rem; margin: 3rem auto; padding: 0 1rem">
                  <h1 style="font-size: 1.4rem">Frontend not built</h1>
                  <p>
                    This backend has no user interface compiled into it:
                    <code>src/Zuil.Host/wwwroot</code> was empty when it was published, so
                    there is no <code>index.html</code> to serve for <code>{requested}</code>.
                  </p>
                  <p>The API itself is running. Two ways to get a screen:</p>
                  <ul>
                    <li>
                      <strong>While developing</strong> — run the Vite dev server and open
                      <a href="http://localhost:5173">localhost:5173</a>. It serves the UI and
                      proxies <code>/api</code> and <code>/hub</code> to this process:
                      <pre style="background: #f4f4f5; padding: .6rem; overflow-x: auto"><code>cd frontend &amp;&amp; npm install &amp;&amp; npm run dev</code></pre>
                    </li>
                    <li>
                      <strong>For a real deployment</strong> — build the single binary, which
                      stages <code>frontend/dist</code> into <code>wwwroot</code> and embeds it:
                      <pre style="background: #f4f4f5; padding: .6rem; overflow-x: auto"><code>./build/build.sh</code></pre>
                    </li>
                  </ul>
                  <p style="color: #52525b">
                    See <code>docs/deploy.md</code>. To check the backend itself, try
                    <code>/api/health</code>.
                  </p>
                </body>
                </html>
                """);
            return;
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync($"""
            Frontend not built.

            This backend has no user interface compiled into it: src/Zuil.Host/wwwroot
            was empty when it was published, so there is no index.html to serve for
            {requested}.

            The API itself is running:

                curl http://localhost:5000/api/health

            Two ways to get a screen:

              While developing — run the Vite dev server and open localhost:5173. It
              serves the UI and proxies /api and /hub to this process:

                  cd frontend && npm install && npm run dev

              For a real deployment — build the single binary, which stages
              frontend/dist into src/Zuil.Host/wwwroot and embeds it:

                  ./build/build.sh

            See docs/deploy.md.

            """);
    }
}
