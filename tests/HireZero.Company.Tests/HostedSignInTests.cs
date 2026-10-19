using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// Hosted sign-in (docs/adr/0020-members-roles-and-sign-in.md) against an in-process fake OIDC provider: the code
// flow with PKCE, a secure session cookie, the first owner bound by verified email, strangers refused, and only the
// public HTTPS origin served through the one trusted proxy. No live identity provider.
public sealed class HostedSignInTests : IAsyncDisposable
{
    private const string Public = "hirezero.example.invalid";
    private const string ClientId = "hirezero-fixture";
    private readonly TemporaryRoot _root = new("hosted-sign-in-");
    private readonly RSA _key = RSA.Create(2048);
    private readonly ConcurrentDictionary<string, (string Nonce, string Challenge, string Email)> _codes = new();
    private string _email = "owner@example.invalid";
    private WebApplication? _idp;
    private string _authority = "";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_idp is not null)
            await _idp.DisposeAsync();
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }

    private async Task StartProviderAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _idp = builder.Build();
        var signing = new RsaSecurityKey(_key) { KeyId = "fixture-key" };
        _idp.MapGet(
            "/.well-known/openid-configuration",
            () =>
                Results.Json(
                    new Dictionary<string, object>
                    {
                        ["issuer"] = _authority,
                        ["authorization_endpoint"] = _authority + "/authorize",
                        ["token_endpoint"] = _authority + "/token",
                        ["jwks_uri"] = _authority + "/jwks",
                        ["response_types_supported"] = new[] { "code" },
                        ["subject_types_supported"] = new[] { "public" },
                        ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
                        ["code_challenge_methods_supported"] = new[] { "S256" },
                    }
                )
        );
        _idp.MapGet(
            "/jwks",
            () =>
            {
                var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(
                    new RsaSecurityKey(_key.ExportParameters(false)) { KeyId = "fixture-key" }
                );
                return Results.Json(
                    new
                    {
                        keys = new[]
                        {
                            new
                            {
                                kty = "RSA",
                                use = "sig",
                                alg = "RS256",
                                kid = jwk.Kid,
                                n = jwk.N,
                                e = jwk.E,
                            },
                        },
                    }
                );
            }
        );
        // Automatic consent: the fixture user is whoever _email names.
        _idp.MapGet(
            "/authorize",
            (HttpRequest request) =>
            {
                var q = request.Query;
                if (q["client_id"] != ClientId || q["code_challenge_method"] != "S256" || q["response_type"] != "code")
                    return Results.BadRequest();
                var code = Guid.NewGuid().ToString("N");
                _codes[code] = (q["nonce"]!, q["code_challenge"]!, _email);
                return Results.Redirect(
                    q["redirect_uri"] + "?code=" + code + "&state=" + Uri.EscapeDataString(q["state"]!)
                );
            }
        );
        _idp.MapPost(
            "/token",
            async (HttpRequest request) =>
            {
                var form = await request.ReadFormAsync();
                if (!_codes.TryRemove(form["code"]!, out var grant))
                    return Results.BadRequest(new { error = "invalid_grant" });
                var challenge = Base64UrlEncoder.Encode(
                    SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!))
                );
                if (challenge != grant.Challenge)
                    return Results.BadRequest(new { error = "invalid_grant" });
                var now = DateTime.UtcNow;
                var idToken = new JsonWebTokenHandler().CreateToken(
                    new SecurityTokenDescriptor
                    {
                        Issuer = _authority,
                        Audience = ClientId,
                        IssuedAt = now,
                        NotBefore = now,
                        Expires = now.AddMinutes(5),
                        SigningCredentials = new SigningCredentials(signing, SecurityAlgorithms.RsaSha256),
                        Claims = new Dictionary<string, object>
                        {
                            ["sub"] = "sub-" + grant.Email.Split('@')[0],
                            ["email"] = grant.Email,
                            ["email_verified"] = true,
                            ["name"] = "Fictional " + grant.Email.Split('@')[0],
                            ["nonce"] = grant.Nonce,
                        },
                    }
                );
                return Results.Json(
                    new
                    {
                        id_token = idToken,
                        access_token = "fixture",
                        token_type = "Bearer",
                        expires_in = 300,
                    }
                );
            }
        );
        await _idp.StartAsync(Token);
        _authority = _idp
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
    }

    // A browser behind the trusted proxy: forwarded HTTPS headers on every host request, cookies kept by hand
    // because they are Secure.
    private sealed class Browser(HttpClient host, HttpClient idp)
    {
        private readonly Dictionary<string, string> _cookies = [];

        public async Task<HttpResponseMessage> Host(HttpMethod method, string path, bool forwarded = true)
        {
            var request = new HttpRequestMessage(method, path);
            if (forwarded)
            {
                request.Headers.Add("X-Forwarded-Proto", "https");
                request.Headers.Add("X-Forwarded-Host", Public);
            }
            if (_cookies.Count > 0)
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value)));
            if (method != HttpMethod.Get)
            {
                request.Headers.Add("Origin", "https://" + Public);
                request.Headers.Add("X-Dev-Request", "1");
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }
            var response = await host.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var set))
                foreach (var cookie in set)
                {
                    var pair = cookie.Split(';')[0].Split('=', 2);
                    if (cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                        _cookies.Remove(pair[0]);
                    else
                        _cookies[pair[0]] = pair[1];
                }
            return response;
        }

        // Starts sign-in, lets the fake provider consent, and returns the host's answer to the callback.
        public async Task<HttpResponseMessage> SignInAsync()
        {
            var challenge = await Host(HttpMethod.Get, "/signin");
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            var consent = await idp.GetAsync(challenge.Headers.Location);
            Assert.Equal(HttpStatusCode.Redirect, consent.StatusCode);
            var callback = consent.Headers.Location!;
            Assert.Equal(Public, callback.Host);
            return await Host(HttpMethod.Get, callback.PathAndQuery);
        }
    }

    [Fact]
    public async Task MembersSignInThroughGenericOidcAndOthersAreRefused()
    {
        await StartProviderAsync();
        var previous = Environment.GetEnvironmentVariable(ExecutionHold.Variable);
        Environment.SetEnvironmentVariable(ExecutionHold.Variable, "0");
        try
        {
            var hosted = new HostedOptions(
                new Uri("https://" + Public + "/"),
                new Uri("http://127.0.0.1:0"),
                _authority,
                ClientId,
                TrustedProxy: IPAddress.Loopback
            ).Validated();
            await using var app = CompanyWorkspace.TryCreate(
                [CompanyHosting.HostedFlag],
                "owner@example.invalid",
                false,
                Path.Combine(_root.Path, "host"),
                new UnavailableModelProvider(),
                fixtureUrl: "http://127.0.0.1:0",
                fixtureTools: _ => { },
                fixtureHosted: hosted
            )!;
            await app.StartAsync(Token);
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            using var hostClient = new HttpClient(
                new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }
            )
            {
                BaseAddress = new Uri(address),
            };
            using var idpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

            var owner = new Browser(hostClient, idpClient);
            Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Host(HttpMethod.Get, "/api/company")).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await owner.Host(HttpMethod.Get, "/api/session", forwarded: false)).StatusCode
            );

            var signedIn = await owner.SignInAsync();
            Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
            Assert.Equal("/", signedIn.Headers.Location!.OriginalString);
            using (
                var session = JsonDocument.Parse(
                    await (await owner.Host(HttpMethod.Get, "/api/session")).Content.ReadAsStringAsync(Token)
                )
            )
            {
                Assert.True(session.RootElement.GetProperty("signedIn").GetBoolean());
                Assert.Equal(CompanyRoles.Owner, session.RootElement.GetProperty("role").GetString());
                Assert.Equal(_authority + "|sub-owner", session.RootElement.GetProperty("login").GetString());
            }
            Assert.Equal(HttpStatusCode.OK, (await owner.Host(HttpMethod.Get, "/api/company")).StatusCode);

            // Someone the company never invited signs in at the provider but gets no session.
            _email = "stranger@example.invalid";
            var stranger = new Browser(hostClient, idpClient);
            var refused = await stranger.SignInAsync();
            Assert.Equal("/?signin=not-invited", refused.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.Host(HttpMethod.Get, "/api/company")).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await owner.Host(HttpMethod.Post, "/signout")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Host(HttpMethod.Get, "/api/company")).StatusCode);
            await app.StopAsync(Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ExecutionHold.Variable, previous);
        }
    }

    [Fact]
    public void HostedOptionsInsistOnHttps()
    {
        var authority = "https://idp.example.invalid";
        Assert.Throws<InvalidOperationException>(() =>
            new HostedOptions(
                new Uri("http://" + Public + "/"),
                new Uri("http://127.0.0.1:8080"),
                authority,
                ClientId
            ).Validated()
        );
        Assert.Throws<InvalidOperationException>(() =>
            new HostedOptions(
                new Uri("https://" + Public + "/"),
                new Uri("http://10.0.0.5:8080"),
                authority,
                ClientId
            ).Validated()
        );
        Assert.Throws<InvalidOperationException>(() =>
            new HostedOptions(
                new Uri("https://" + Public + "/"),
                new Uri("https://10.0.0.5:8443"),
                authority,
                ClientId
            ).Validated()
        );
        Assert.Throws<InvalidOperationException>(() =>
            new HostedOptions(
                new Uri("https://" + Public + "/"),
                new Uri("http://127.0.0.1:8080"),
                "http://idp.example.invalid",
                ClientId
            ).Validated()
        );
        Assert.NotNull(
            new HostedOptions(
                new Uri("https://" + Public + "/"),
                new Uri("http://10.0.0.5:8080"),
                authority,
                ClientId,
                TrustedProxy: IPAddress.Parse("10.0.0.1")
            ).Validated()
        );
    }
}
