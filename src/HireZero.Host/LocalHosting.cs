using System.Net;

// Plumbing shared by every composition profile: loopback-only listeners and the local request boundary.
public static class LocalHosting
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";

    // Parses an explicit listener address and refuses anything but HTTP on a loopback IP.
    public static Uri LoopbackUri(string url, string refusal)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            !IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address))
            throw new InvalidOperationException(refusal);
        return uri;
    }

    // Replaces the default endpoint loader so args, appsettings or environment cannot add or reload a listener.
    // Without a fixture the profile binds both loopback families on its fixed port.
    public static void ListenLoopbackOnly(this WebApplicationBuilder builder, Uri? fixture, int port, long maxRequestBodySize)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
            options.Limits.MaxRequestBodySize = maxRequestBodySize;
            if (fixture is not null) options.Listen(IPAddress.Parse(fixture.Host), fixture.Port);
            else { options.Listen(IPAddress.Loopback, port); options.Listen(IPAddress.IPv6Loopback, port); }
        });
    }

    public static void ApplySecurityHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
    }

    // A loopback caller addressing the exact expected host, with no proxy or tailnet identity headers.
    public static bool IsDirectLocalRequest(HttpContext context, string expectedHost) =>
        context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote) &&
        context.Request.Host.Value == expectedHost &&
        !context.Request.Headers.Keys.Any(key =>
            key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("Tailscale-", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase));
}
