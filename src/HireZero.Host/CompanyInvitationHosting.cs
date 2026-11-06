using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

// Links carry their secret in the fragment. The web server and proxy never receive it in a request URL.
public sealed class CompanyInvitationHosting(
    CompanyStore directory,
    Func<string, CompanyStore> open,
    IDataProtectionProvider protection,
    bool enabled
)
{
    private const string Cookie = "__Host-hirezero-join";
    private readonly ITimeLimitedDataProtector _protector = protection
        .CreateProtector("hirezero-company-invitation-v1")
        .ToTimeLimitedDataProtector();

    public sealed record Prepare(string Token);

    public sealed record Accept(string Invitation, int ExpectedVersion);

    public sealed record Invite(string Email, string Role, int ExpectedVersion = 0);

    private string Token(HttpContext context)
    {
        if (!enabled)
            throw new WorkspaceException("Invitations for free companies are currently unavailable.", 403);
        try
        {
            return _protector.Unprotect(context.Request.Cookies[Cookie] ?? "", out _);
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        {
            throw new WorkspaceException(
                "Open your invitation link again. This invitation session is missing or expired.",
                410
            );
        }
    }

    private CompanyStore Store(string token) => open(directory.InvitationCompany(token));

    public bool CanSignIn(HttpContext context)
    {
        try
        {
            var token = Token(context);
            Store(token).PreviewInvitation(token);
            return true;
        }
        catch (WorkspaceException)
        {
            return false;
        }
    }

    // Match the router's case/trailing-slash behavior, but exempt only the operations guests actually need.
    private static bool Route(HttpRequest request, string method, string path) =>
        request.Method == method
        && string.Equals(request.Path.Value?.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase);

    public static bool Anonymous(HttpRequest request) =>
        Route(request, "GET", "/api/join") || Route(request, "POST", "/api/join/prepare");

    public static bool WithoutMembership(HttpRequest request) =>
        Anonymous(request) || Route(request, "POST", "/api/join/accept");

    public static void AddLimits(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/api/join")
                || context.Request.Path.StartsWithSegments("/join")
                || context.Request.Path.StartsWithSegments("/api/company/member-invites")
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }
                    )
                    : RateLimitPartition.GetNoLimiter("other")
            );
            options.OnRejected = async (context, cancellation) =>
            {
                // This middleware can finish before the workspace's usual response headers run.
                LocalHosting.ApplySecurityHeaders(context.HttpContext);
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Too many invitation requests. Wait a minute before trying again." },
                    cancellation
                );
            };
        });
    }

    public void Map(WebApplication app, Func<CompanyStore> current, Uri publicUrl)
    {
        app.MapPost(
            "/api/company/member-invites",
            (Invite request, HttpContext context) =>
            {
                if (!enabled)
                    throw new WorkspaceException("Invitations for free companies are currently unavailable.", 403);
                var invitation = current()
                    .InviteSignupMember(
                        CompanyHosting.Subject(context)!,
                        request.Email,
                        request.Role,
                        request.ExpectedVersion
                    );
                return Results.Ok(
                    new { invitation.Member, link = new Uri(publicUrl, "join").AbsoluteUri + "#" + invitation.Token }
                );
            }
        );
        app.MapPost(
            "/api/join/prepare",
            (Prepare request, HttpContext context) =>
            {
                if (!enabled)
                    throw new WorkspaceException("Invitations for free companies are currently unavailable.", 403);
                // Validation precedes the cookie: an invalid visit cannot replace a valid invitation session.
                Store(request.Token).PreviewInvitation(request.Token);
                context.Response.Cookies.Append(
                    Cookie,
                    _protector.Protect(request.Token, TimeSpan.FromMinutes(15)),
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/",
                        MaxAge = TimeSpan.FromMinutes(15),
                        IsEssential = true,
                    }
                );
                return Results.Ok(new { prepared = true });
            }
        );
        app.MapGet(
            "/api/join",
            (HttpContext context) =>
            {
                var token = Token(context);
                var store = Store(token);
                var preview = store.PreviewInvitation(token);
                var subject = CompanyHosting.Subject(context);
                var email = context.User.FindFirst("email")?.Value;
                return Results.Ok(
                    new
                    {
                        preview.CompanyId,
                        preview.CompanyName,
                        preview.Role,
                        preview.Version,
                        preview.Invitation,
                        signedIn = subject is not null,
                        browserScope = subject is null ? null : CompanyHosting.BrowserScope(subject),
                        accountEmail = subject is null ? null : email,
                        matches = subject is not null
                            && store.InvitationMatches(token, email, CompanySignupHosting.Verified(context)),
                    }
                );
            }
        );
        app.MapPost(
            "/api/join/accept",
            (Accept request, HttpContext context) =>
            {
                var token = Token(context);
                var subject = CompanyHosting.Subject(context) ?? throw new WorkspaceException("Sign in first.", 401);
                if (context.Request.Headers[CompanyHosting.BrowserScopeHeader] != CompanyHosting.BrowserScope(subject))
                    throw new WorkspaceException(
                        "The signed-in account changed. Reload the invitation before continuing.",
                        409
                    );
                // Another tab may have opened a different invitation. Never join the company the user did not review.
                if (request.Invitation != CompanyStore.InvitationHash(token))
                    throw new WorkspaceException(
                        "The invitation changed in another tab. Reload and review it before joining.",
                        409
                    );
                var joined = Store(token)
                    .AcceptInvitation(
                        token,
                        subject,
                        context.User.FindFirst("email")?.Value,
                        CompanySignupHosting.Verified(context),
                        context.User.FindFirst("name")?.Value,
                        request.ExpectedVersion
                    );
                context.Response.Cookies.Delete(
                    Cookie,
                    new CookieOptions
                    {
                        Secure = true,
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/",
                    }
                );
                return Results.Ok(joined);
            }
        );
    }
}
