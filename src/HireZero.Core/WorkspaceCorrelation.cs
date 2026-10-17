namespace HireZero.Core;

// Only opaque request IDs, method, path and status are logged; no body, query, approval snapshot or credentials.
public static class WorkspaceCorrelation
{
    public const string Header = "X-HireZero-Request-Id";

    public static void UseWorkspaceCorrelation(this WebApplication app)
    {
        app.Use(
            async (context, next) =>
            {
                var supplied = context.Request.Headers[Header].ToString();
                context.TraceIdentifier = Guid.TryParseExact(supplied, "D", out var parsed)
                    ? parsed.ToString("D")
                    : Guid.NewGuid().ToString("D");
                context.Response.Headers[Header] = context.TraceIdentifier;
                var status = 500;
                try
                {
                    await next();
                    status = context.Response.StatusCode;
                }
                finally
                {
                    if (context.Request.Method is not ("GET" or "HEAD"))
                        app.Logger.LogInformation(
                            "HireZero request {RequestId} {Method} ended with {Status}",
                            context.TraceIdentifier,
                            context.Request.Method,
                            status
                        );
                }
            }
        );
    }
}
