using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

// Hosted company mode (docs/adr/0020-members-roles-and-sign-in.md): members sign in through any standards-compliant
// OIDC provider with the authorization code flow and PKCE, and the session is an HTTP-only, secure, same-site cookie.
// The public URL is always HTTPS. The host either serves HTTPS itself (a certificate file) or sits behind one
// explicitly trusted reverse proxy, whose forwarded headers are the only ones believed.
public sealed record HostedOptions(
    Uri PublicUrl,
    Uri Listen,
    string Authority,
    string ClientId,
    string? ClientSecret = null,
    string[]? Scopes = null,
    IPAddress? TrustedProxy = null,
    string? CertificatePath = null,
    string? CertificatePassword = null
)
{
    public const string SubjectClaim = "hirezero:subject";

    // Read from the environment; the client secret and certificate password never come from a file in the repo.
    public static HostedOptions FromEnvironment()
    {
        string Need(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException(name + " is required for hosted mode.");
        return new HostedOptions(
            new Uri(Need("HIREZERO_PUBLIC_URL")),
            new Uri(Need("HIREZERO_LISTEN")),
            Need("HIREZERO_OIDC_AUTHORITY"),
            Need("HIREZERO_OIDC_CLIENT_ID"),
            Environment.GetEnvironmentVariable("HIREZERO_OIDC_CLIENT_SECRET"),
            Environment
                .GetEnvironmentVariable("HIREZERO_OIDC_SCOPES")
                ?.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Environment.GetEnvironmentVariable("HIREZERO_TRUSTED_PROXY") is { Length: > 0 } proxy
                ? IPAddress.Parse(proxy)
                : null,
            Environment.GetEnvironmentVariable("HIREZERO_TLS_CERTIFICATE"),
            Environment.GetEnvironmentVariable("HIREZERO_TLS_CERTIFICATE_PASSWORD")
        ).Validated();
    }

    public HostedOptions Validated()
    {
        if (PublicUrl.Scheme != "https" || PublicUrl.AbsolutePath != "/")
            throw new InvalidOperationException("The public URL must be an https origin.");
        var authority = new Uri(Authority);
        if (authority.Scheme != "https" && !IsLoopback(authority))
            throw new InvalidOperationException("The OIDC authority must use https.");
        var loopback = IsLoopback(Listen);
        if (Listen.Scheme == "https" && CertificatePath is null)
            throw new InvalidOperationException("An https listener needs HIREZERO_TLS_CERTIFICATE.");
        if (Listen.Scheme == "http" && TrustedProxy is null && !loopback)
            throw new InvalidOperationException(
                "Beyond loopback, listen with https or name the reverse proxy in HIREZERO_TRUSTED_PROXY."
            );
        if (Listen.Scheme is not ("http" or "https") || !IPAddress.TryParse(Listen.Host, out _))
            throw new InvalidOperationException("Listen on an explicit IP address, for example http://127.0.0.1:8080.");
        return this;
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
}

public static class CompanyHosting
{
    public const string HostedFlag = "--company-hosted";

    public static void ListenHosted(this WebApplicationBuilder builder, HostedOptions options, long maxRequestBodySize)
    {
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
            kestrel.Limits.MaxRequestBodySize = maxRequestBodySize;
            kestrel.Listen(
                IPAddress.Parse(options.Listen.Host),
                options.Listen.Port,
                listen =>
                {
                    if (options.Listen.Scheme == "https")
                        listen.UseHttps(options.CertificatePath!, options.CertificatePassword);
                }
            );
        });
    }

    public static void AddHostedSignIn(this IServiceCollection services, HostedOptions options)
    {
        services
            .AddAuthentication(auth =>
            {
                auth.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                auth.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = "__Host-hirezero";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
                cookie.SlidingExpiration = true;
                // The API answers 401 instead of redirecting, so the page can send the browser to /signin.
                cookie.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = 401;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(oidc =>
            {
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.UsePkce = true;
                oidc.ResponseMode = OpenIdConnectResponseMode.Query;
                oidc.RequireHttpsMetadata = new Uri(options.Authority).Scheme == "https";
                oidc.SaveTokens = false;
                oidc.GetClaimsFromUserInfoEndpoint = false;
                oidc.MapInboundClaims = false;
                oidc.CallbackPath = "/signin-oidc";
                oidc.Scope.Clear();
                foreach (var scope in options.Scopes ?? ["openid", "profile", "email"])
                    oidc.Scope.Add(scope);
                oidc.Events.OnTokenValidated = context =>
                {
                    var principal = context.Principal!;
                    string? Claim(string type) => principal.FindFirst(type)?.Value;
                    var issuer = Claim("iss");
                    var sub = Claim("sub");
                    if (issuer is null || sub is null)
                    {
                        context.Fail("The provider did not identify the user.");
                        return Task.CompletedTask;
                    }
                    var membership = context.HttpContext.RequestServices.GetRequiredService<CompanyMembership>();
                    var actor = membership.SignIn!(
                        issuer + "|" + sub,
                        Claim("email"),
                        string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase),
                        Claim("name")
                    );
                    if (actor is null)
                    {
                        context.Fail("not-invited");
                        return Task.CompletedTask;
                    }
                    context.Principal = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim(HostedOptions.SubjectClaim, actor.Subject),
                                new Claim("name", Claim("name") ?? ""),
                            ],
                            CookieAuthenticationDefaults.AuthenticationScheme
                        )
                    );
                    return Task.CompletedTask;
                };
                oidc.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect(
                        "/?signin=" + (context.Failure?.Message == "not-invited" ? "not-invited" : "failed")
                    );
                    return Task.CompletedTask;
                };
            });
    }

    // Forwarded headers are believed only from the one trusted proxy; then the cookie and sign-in routes.
    public static void UseHostedSignIn(this WebApplication app, HostedOptions options)
    {
        if (options.TrustedProxy is not null)
        {
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            };
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Add(options.TrustedProxy);
            forwarded.AllowedHosts = [options.PublicUrl.Authority];
            app.UseForwardedHeaders(forwarded);
        }
        app.UseAuthentication();
    }

    public static void MapHostedSignIn(this WebApplication app)
    {
        app.MapGet(
            "/signin",
            () =>
                Results.Challenge(
                    new AuthenticationProperties { RedirectUri = "/" },
                    [OpenIdConnectDefaults.AuthenticationScheme]
                )
        );
        app.MapPost(
            "/signout",
            async (HttpContext context) =>
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Ok(new { signedOut = true });
            }
        );
    }

    // The signed-in member's subject, or null when the request has no session.
    public static string? Subject(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirst(HostedOptions.SubjectClaim)?.Value
            : null;
}

// One company's runtime in this host: its store, tool registry and scheduler.
public sealed record CompanyRuntime(CompanyStore Store, ToolRegistry Tools, CompanyScheduler Scheduler);

// Hosted sign-in across the data root's companies, set once their runtimes exist.
public sealed class CompanyMembership
{
    public Func<string, string?, bool, string?, CompanyActor?>? SignIn { get; set; }
}

public sealed record CompanyName(string? Name);
