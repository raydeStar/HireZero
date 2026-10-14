using System.Net;

namespace HireZero.Core;

// Identity and request hygiene for every /api route: local owner on loopback, an invited identity forwarded by
// HTTPS Tailscale Serve, or (without Serve) a caller on the tailnet listener identified by tailscale whois.
// Mutations need a same-origin JSON request with X-Dev-Request.
public static class WorkspaceAccess
{
    public static string Login(HttpContext context) => (string)context.Items["login"]!;
    public static string Role(HttpContext context) => (string)context.Items["role"]!;
    public static bool IsOwner(HttpContext context) => Role(context) == "owner";
    public static bool CanRead(HttpContext context, WorkRequest item) => IsOwner(context) || item.Requester == Login(context);

    public static void UseWorkspaceAccess(this WebApplication app, string ownerLogin, bool localOwnerEnabled, string localHost)
    {
        var tailnet = app.Services.GetService<TailnetAccess>();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";

            if (context.Request.ContentLength > 16_384) { context.Response.StatusCode = 413; return; }
            // The tailnet listener answers only to this computer's own tailnet names (no DNS rebinding), and never trusts forwarded headers.
            var onTailnet = tailnet is not null && tailnet.Arrived(context);
            if (onTailnet && (!tailnet!.KnownHost(context.Request.Host.Value ?? "") || context.Request.Headers.ContainsKey("X-Forwarded-For") ||
                context.Request.Headers.ContainsKey("X-Forwarded-Host"))) { context.Response.StatusCode = 403; return; }
            if (!context.Request.Path.StartsWithSegments("/api")) { await next(); return; }
            var remote = context.Connection.RemoteIpAddress;
            var forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString();
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
            var identity = context.Request.Headers["Tailscale-User-Login"].ToString().Trim().ToLowerInvariant();
            var isLocalOwner = localOwnerEnabled && remote is not null && IPAddress.IsLoopback(remote) &&
                context.Request.Host.Value == localHost &&
                string.IsNullOrEmpty(forwardedHost) && string.IsNullOrEmpty(forwardedFor) &&
                string.IsNullOrEmpty(identity) && string.IsNullOrEmpty(context.Request.Headers["X-Forwarded-Proto"]);
            var hasServeIdentity = remote is not null && IPAddress.IsLoopback(remote) &&
                context.Request.Headers["X-Forwarded-Proto"] == "https" &&
                forwardedHost.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(forwardedFor) && identity.Length is > 0 and <= 320 && identity.Contains('@');
            if (onTailnet)
            {
                // Whois is the only identity source here; a Tailscale-User-Login header from the caller is ignored.
                identity = await tailnet!.IdentifyAsync(remote) ?? "";
                isLocalOwner = false; hasServeIdentity = false;
                if (identity.Length == 0) { context.Response.StatusCode = 403; return; }
            }
            else if (!hasServeIdentity && !isLocalOwner) { context.Response.StatusCode = 403; return; }
            if (isLocalOwner) identity = ownerLogin;
            var role = identity == ownerLogin ? "owner" :
                app.Services.GetRequiredService<RequestStore>().FindPerson(identity) is { Revoked: false } person ? PersonRoles.Normalize(person.Role) : null;
            if (role is null && app.Services.GetService<AccessDesk>() is { } desk)
            {
                role = desk.Knock(identity, tailnet?.DisplayName(identity) ?? context.Request.Headers["Tailscale-User-Name"].ToString());
                if (role is null)
                {
                    if (context.Request.Path.Equals("/api/session", StringComparison.OrdinalIgnoreCase) && context.Request.Method == "GET")
                    { await context.Response.WriteAsJsonAsync(new { login = identity, role = "pending", accessMode = onTailnet ? "tailnet" : "tailscale" }); return; }
                    context.Response.StatusCode = 403; return;
                }
            }
            if (role is null) { context.Response.StatusCode = 403; return; }
            context.Items["login"] = identity;
            context.Items["role"] = role;

            // The MCP endpoint serves programs (a person's own AI), not browsers: JSON only, and any request a browser
            // would send (it always carries Origin on a cross-site POST) is refused.
            if (context.Request.Path.Equals("/api/mcp", StringComparison.OrdinalIgnoreCase) && context.Request.Method == "POST")
            {
                if (context.Request.Headers.ContainsKey("Origin") || !context.Request.HasJsonContentType()) { context.Response.StatusCode = 403; return; }
            }
            else if (context.Request.Method is not ("GET" or "HEAD"))
            {
                var requestOrigin = context.Request.Headers.Origin.ToString();
                var expectedOrigin = onTailnet ? "http://" + context.Request.Host.Value : isLocalOwner ? "http://" + localHost : $"https://{forwardedHost}";
                if (requestOrigin != expectedOrigin ||
                    context.Request.Headers["X-Dev-Request"] != "1" ||
                    !context.Request.HasJsonContentType())
                { context.Response.StatusCode = 403; return; }
            }
            // Expected refusals carry a message the owner can read; they are never a bare 500.
            try { await next(); }
            catch (Exception error) when (error is WorkspaceException or ModelUnavailableException && !context.Response.HasStarted)
            {
                context.Response.StatusCode = error is WorkspaceException known ? known.Status : 503;
                await context.Response.WriteAsJsonAsync(new { error = error.Message });
            }
        });
    }
}
