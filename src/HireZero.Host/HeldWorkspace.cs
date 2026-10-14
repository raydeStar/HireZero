using System.Net;
using HireZero.Core;

// A separate composition root: held startup never constructs normal providers, stores or workers.
public static class HeldWorkspace
{
    public const string Variable = "HIREZERO_EXECUTION_HOLD";
    public const string Message = "Execution is held for verification. Tasks and approvals have not been resumed. The owner must approve resumption after verification.";

    public static bool Requested(string? value) => value switch
    {
        null or "" or "0" => false,
        "1" => true,
        _ => throw new InvalidOperationException(Variable + " must be 1 or 0.")
    };

    public static WebApplication? TryCreate(string[] args, string owner, bool localOwner, string dataRoot,
        string? fixtureUrl = null, string? fixtureContentRoot = null)
    {
        if (!Requested(Environment.GetEnvironmentVariable(Variable))) return null;
        const string localHost = "127.0.0.1:5064";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = fixtureContentRoot });
        // The optional fixture address is only a local test seam; normal held startup binds the existing loopback port.
        if (fixtureUrl is not null && (!Uri.TryCreate(fixtureUrl, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            !IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address)))
            throw new InvalidOperationException("A held fixture must use an HTTP loopback address.");
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Replace the default endpoint loader: args/appsettings/environment cannot add or reload a public listener.
            options.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
            options.Limits.MaxRequestBodySize = 16_384;
            if (fixtureUrl is not null) { var fixture = new Uri(fixtureUrl); options.Listen(IPAddress.Parse(fixture.Host), fixture.Port); }
            else { options.Listen(IPAddress.Loopback, 5064); options.Listen(IPAddress.IPv6Loopback, 5064); }
        });
        var runtime = new WorkspaceRuntime(owner, Path.GetFullPath(dataRoot), null, [], localOwner) { ExecutionHeld = true };
        var app = builder.Build();
        app.UseWorkspaceCorrelation();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";
            // Held status is local only; even a forwarded, invited identity cannot reach normal dispatch.
            if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
                context.Request.Host.Value != localHost || context.Request.Headers.Keys.Any(key =>
                    key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Tailscale-", StringComparison.OrdinalIgnoreCase) || key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)))
            { context.Response.StatusCode = 403; return; }
            var path = context.Request.Path.Value?.TrimEnd('/') ?? "";
            if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                if (!localOwner) { context.Response.StatusCode = 403; return; }
                if (context.Request.Method is not ("GET" or "HEAD") ||
                    !path.Equals("/api/runtime", StringComparison.OrdinalIgnoreCase) && !path.Equals("/api/session", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 503;
                    await context.Response.WriteAsJsonAsync(new { error = Message, code = "execution-held", correlationId = context.TraceIdentifier });
                    return;
                }
            }
            else if (context.Request.Method is not ("GET" or "HEAD"))
            { context.Response.StatusCode = 503; return; }
            await next();
        });
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", executionHeld = true }));
        app.MapGet("/api/runtime", () => Results.Ok(runtime));
        app.MapGet("/api/session", () => Results.Ok(new { login = owner, role = "owner", accessMode = "local", agentModel = "Execution held", agentReasoning = "", roles = Array.Empty<string>(), executionHeld = true }));
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallback("/api/{**path}", () => Results.StatusCode(503));
        app.MapFallbackToFile("index.html");
        return app;
    }
}
