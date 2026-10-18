using HireZero.Core;

// A separate composition root: held startup never constructs normal providers, stores or workers.
public static class HeldWorkspace
{
    public const string Variable = ExecutionHold.Variable;
    public const string Message =
        "Execution is held for verification. Tasks and approvals have not been resumed. The owner must approve resumption after verification.";

    public static bool Requested(string? value) => ExecutionHold.Requested(value);

    public static WebApplication? TryCreate(
        string[] args,
        string owner,
        bool localOwner,
        string dataRoot,
        string? fixtureUrl = null,
        string? fixtureContentRoot = null
    )
    {
        if (!Requested(Environment.GetEnvironmentVariable(Variable)))
            return null;
        const string localHost = "127.0.0.1:5064";
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { Args = args, ContentRootPath = fixtureContentRoot }
        );
        // The optional fixture address is only a local test seam; normal held startup binds the existing loopback port.
        var fixture = fixtureUrl is null
            ? null
            : LocalHosting.LoopbackUri(fixtureUrl, "A held fixture must use an HTTP loopback address.");
        builder.ListenLoopbackOnly(fixture, 5064, 16_384);
        var runtime = new WorkspaceRuntime(owner, Path.GetFullPath(dataRoot), null, [], localOwner)
        {
            ExecutionHeld = true,
        };
        var app = builder.Build();
        app.UseWorkspaceCorrelation();
        app.Use(
            async (context, next) =>
            {
                LocalHosting.ApplySecurityHeaders(context);
                // Held status is local only; even a forwarded, invited identity cannot reach normal dispatch.
                if (!LocalHosting.IsDirectLocalRequest(context, localHost))
                {
                    context.Response.StatusCode = 403;
                    return;
                }
                var path = context.Request.Path.Value?.TrimEnd('/') ?? "";
                if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                {
                    if (!localOwner)
                    {
                        context.Response.StatusCode = 403;
                        return;
                    }
                    if (
                        context.Request.Method is not ("GET" or "HEAD")
                        || !path.Equals("/api/runtime", StringComparison.OrdinalIgnoreCase)
                            && !path.Equals("/api/session", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        context.Response.StatusCode = 503;
                        await context.Response.WriteAsJsonAsync(
                            new
                            {
                                error = Message,
                                code = "execution-held",
                                correlationId = context.TraceIdentifier,
                            }
                        );
                        return;
                    }
                }
                else if (context.Request.Method is not ("GET" or "HEAD"))
                {
                    context.Response.StatusCode = 503;
                    return;
                }
                await next();
            }
        );
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", executionHeld = true }));
        app.MapGet("/api/runtime", () => Results.Ok(runtime));
        app.MapGet(
            "/api/session",
            () =>
                Results.Ok(
                    new
                    {
                        login = owner,
                        role = "owner",
                        accessMode = "local",
                        agentModel = "Execution held",
                        agentReasoning = "",
                        roles = Array.Empty<string>(),
                        executionHeld = true,
                    }
                )
        );
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallback("/api/{**path}", () => Results.StatusCode(503));
        app.MapFallbackToFile("index.html");
        return app;
    }
}
