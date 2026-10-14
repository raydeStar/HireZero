using System.Globalization;
using System.Threading.RateLimiting;

namespace HireZero.Core;

public static class WorkspaceRateLimits
{
    public static IServiceCollection AddWorkspaceRateLimits(this IServiceCollection services) => services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var remaining)
                ? Math.Clamp((int)Math.Ceiling(remaining.TotalSeconds), 1, 60) : 60;
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = $"The workspace request limit was reached. Try again in {seconds} seconds.",
                code = "workspace_rate_limited",
                retryAfterSeconds = seconds
            }, cancellationToken);
        };
        options.AddPolicy("agent", context => Window(Identity(context), 3));
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            // Page assets never consume API capacity; polling must not consume action capacity.
            if (!context.Request.Path.StartsWithSegments("/api")) return RateLimitPartition.GetNoLimiter("assets");
            var read = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            return Window(Identity(context) + (read ? ":read" : ":write"), read ? 600 : 60);
        });
    });

    private static string Identity(HttpContext context) => context.Items["login"] as string ?? "unauthenticated";

    private static RateLimitPartition<string> Window(string key, int permits) => RateLimitPartition.GetFixedWindowLimiter(key,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
}
