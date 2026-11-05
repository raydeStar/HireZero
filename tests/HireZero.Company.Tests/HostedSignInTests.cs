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
    private bool _verified = true;
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
                            ["email_verified"] = _verified,
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

        public async Task<HttpResponseMessage> Host(
            HttpMethod method,
            string path,
            bool forwarded = true,
            string? memberScope = null,
            string? company = null,
            string body = "{}",
            bool csrf = true
        )
        {
            var request = new HttpRequestMessage(method, path);
            if (memberScope is not null)
                request.Headers.Add(CompanyHosting.BrowserScopeHeader, memberScope);
            if (company is not null)
                request.Headers.Add("X-HireZero-Company", company);
            if (forwarded)
            {
                request.Headers.Add("X-Forwarded-Proto", "https");
                request.Headers.Add("X-Forwarded-Host", Public);
            }
            if (_cookies.Count > 0)
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value)));
            if (method != HttpMethod.Get)
            {
                if (csrf)
                    request.Headers.Add("Origin", "https://" + Public);
                request.Headers.Add("X-Dev-Request", "1");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
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
    public async Task OpenSignupUsesVerifiedOidcAndKeepsCompanyDataAndIntegrationsIsolated()
    {
        await StartProviderAsync();
        var hosted = new HostedOptions(
            new Uri("https://" + Public + "/"),
            new Uri("http://127.0.0.1:0"),
            _authority,
            ClientId,
            TrustedProxy: IPAddress.Loopback
        ).Validated();
        var settings = CompanySettings.Load(n => n == "HIREZERO_OPEN_SIGNUP" ? "1" : null);
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyHosting.HostedFlag],
            "owner@example.invalid",
            false,
            Path.Combine(_root.Path, "signup-host"),
            new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            fixtureHosted: hosted,
            settings: settings
        )!;
        await app.StartAsync(Token);
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        using var host = new HttpClient(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
            }
        )
        {
            BaseAddress = new Uri(address),
        };
        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        var guest = new Browser(host, idp);
        _email = "guest@example.invalid";
        _verified = false;
        Assert.Equal("/?signin=not-invited", (await guest.SignInAsync()).Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.Host(HttpMethod.Post, "/api/signup")).StatusCode);
        _verified = true;
        Assert.Equal("/", (await guest.SignInAsync()).Headers.Location!.OriginalString);
        using var session = JsonDocument.Parse(
            await (await guest.Host(HttpMethod.Get, "/api/session")).Content.ReadAsStringAsync(Token)
        );
        Assert.True(session.RootElement.GetProperty("openSignup").GetBoolean());
        Assert.Empty(session.RootElement.GetProperty("companies").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.Host(HttpMethod.Get, "/api/company")).StatusCode);
        var scope = session.RootElement.GetProperty("browserScope").GetString()!;
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await guest.Host(HttpMethod.Post, "/api/signup", csrf: false)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await guest.Host(HttpMethod.Post, "/api/signup", memberScope: "stale")).StatusCode
        );
        using var created = JsonDocument.Parse(
            await (await guest.Host(HttpMethod.Post, "/api/signup", memberScope: scope)).Content.ReadAsStringAsync(
                Token
            )
        );
        var company = created.RootElement.GetProperty("id").GetString()!;
        using var again = JsonDocument.Parse(
            await (await guest.Host(HttpMethod.Post, "/api/signup", memberScope: scope)).Content.ReadAsStringAsync(
                Token
            )
        );
        Assert.Equal(company, again.RootElement.GetProperty("id").GetString());
        using var snapshot = JsonDocument.Parse(
            await (await guest.Host(HttpMethod.Get, "/api/company", company: company)).Content.ReadAsStringAsync(Token)
        );
        var capabilities = snapshot.RootElement.GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("signupCompany").GetBoolean());
        Assert.Equal(50, capabilities.GetProperty("freeAllowance").GetProperty("remainingCents").GetDecimal());
        Assert.Empty(capabilities.GetProperty("integrations").EnumerateArray());
        Assert.False(capabilities.GetProperty("businessSearch").GetBoolean());
        var roles = snapshot
            .RootElement.GetProperty("data")
            .GetProperty("roles")
            .EnumerateArray()
            .Select(r => r.GetProperty("id").GetString())
            .ToArray();
        Assert.Contains("marketing", roles);
        Assert.DoesNotContain("developer", roles);
        using var department = JsonDocument.Parse(
            await (
                await guest.Host(
                    HttpMethod.Post,
                    "/api/company/commands",
                    company: company,
                    body: CompanyJson.Encode(
                        new CompanyCommand(Guid.NewGuid().ToString("D"), "department.create")
                        {
                            Name = "Fictional Marketing",
                        }
                    )
                )
            ).Content.ReadAsStringAsync(Token)
        );
        var departmentId = department.RootElement.GetProperty("id").GetString();
        var hire = new CompanyCommand(Guid.NewGuid().ToString("D"), "employee.hire")
        {
            DepartmentId = departmentId,
            RoleId = "marketing",
            Name = "Fictional marketer",
            Responsibility = "Draft private marketing plans.",
        };
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await guest.Host(
                    HttpMethod.Post,
                    "/api/company/commands",
                    company: company,
                    body: CompanyJson.Encode(hire)
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (
                await guest.Host(
                    HttpMethod.Post,
                    "/api/company/commands",
                    company: company,
                    body: CompanyJson.Encode(
                        hire with
                        {
                            RequestId = Guid.NewGuid().ToString("D"),
                            RoleId = "developer",
                        }
                    )
                )
            ).StatusCode
        );

        foreach (
            var path in new[]
            {
                "/api/company/integrations/setup",
                "/api/connections/google",
                "/api/company/model-connection",
            }
        )
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await guest.Host(HttpMethod.Get, path, company: company)).StatusCode
            );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await guest.Host(HttpMethod.Post, "/api/companies", company: company)).StatusCode
        );
        var root = app.Services.GetRequiredService<CompanyStore>();
        var listing = CompanyStore.Companies(root.Database).Single(c => c.Id == company);
        var store = new CompanyStore(
            root.Database,
            listing.Creator,
            CompanyWorkspace.Definitions(),
            companyId: company
        );
        Assert.Equal("owner", store.Actor(_authority + "|sub-guest").Role);
        _email = "other@example.invalid";
        var other = new Browser(host, idp);
        await other.SignInAsync();
        using var otherCreated = JsonDocument.Parse(
            await (await other.Host(HttpMethod.Post, "/api/signup")).Content.ReadAsStringAsync(Token)
        );
        var second = otherCreated.RootElement.GetProperty("id").GetString()!;
        Assert.NotEqual(company, second);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await other.Host(HttpMethod.Get, "/api/company", company: company)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await guest.Host(HttpMethod.Get, "/api/company", company: second)).StatusCode
        );
        using var choices = JsonDocument.Parse(
            await (await other.Host(HttpMethod.Get, "/api/companies")).Content.ReadAsStringAsync(Token)
        );
        Assert.Equal(second, Assert.Single(choices.RootElement.EnumerateArray()).GetProperty("id").GetString());
        await app.StopAsync(Token);
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
            var originalScope = CompanyHosting.BrowserScope(_authority + "|sub-owner");
            Assert.Equal(
                HttpStatusCode.OK,
                (await owner.Host(HttpMethod.Get, "/api/company", memberScope: originalScope)).StatusCode
            );

            // Someone the company never invited signs in at the provider but gets no session.
            _email = "stranger@example.invalid";
            var stranger = new Browser(hostClient, idpClient);
            var refused = await stranger.SignInAsync();
            Assert.Equal("/?signin=not-invited", refused.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.Host(HttpMethod.Get, "/api/company")).StatusCode);

            // The same cookie jar signs in as another invited owner. A stale tab cannot act with that new identity.
            var store = app.Services.GetRequiredService<CompanyStore>();
            store.Apply(
                new CompanyCommand(Guid.NewGuid().ToString("D"), "member.invite")
                {
                    Body = "second@example.invalid",
                    Decision = CompanyRoles.Owner,
                },
                _authority + "|sub-owner",
                out _
            );
            _email = "second@example.invalid";
            Assert.Equal(HttpStatusCode.Redirect, (await owner.SignInAsync()).StatusCode);
            using var changedSession = JsonDocument.Parse(
                await (
                    await owner.Host(HttpMethod.Get, "/api/session", memberScope: originalScope)
                ).Content.ReadAsStringAsync(Token)
            );
            var nextScope = changedSession.RootElement.GetProperty("browserScope").GetString()!;
            Assert.NotEqual(originalScope, nextScope);
            Assert.Equal(CompanyHosting.BrowserScope(_authority + "|sub-second"), nextScope);
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
            {
                var refusedStale = await owner.Host(
                    method,
                    method == HttpMethod.Get ? "/api/company" : "/api/company/commands",
                    memberScope: originalScope
                );
                Assert.Equal(HttpStatusCode.Conflict, refusedStale.StatusCode);
                using var refusal = JsonDocument.Parse(await refusedStale.Content.ReadAsStringAsync(Token));
                Assert.Equal("session-changed", refusal.RootElement.GetProperty("code").GetString());
            }
            Assert.Equal(
                HttpStatusCode.OK,
                (await owner.Host(HttpMethod.Get, "/api/company", memberScope: nextScope)).StatusCode
            );

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
    public async Task SecondaryCompanyInviteAndRevocationKeepAnAuthenticatedWayHome()
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
            using var hostClient = new HttpClient(
                new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    UseProxy = false,
                }
            )
            {
                BaseAddress = new Uri(
                    app.Services.GetRequiredService<IServer>()
                        .Features.Get<IServerAddressesFeature>()!
                        .Addresses.First()
                ),
            };
            using var idpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
            var owner = new Browser(hostClient, idpClient);
            Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Host(HttpMethod.Get, "/api/companies")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await owner.SignInAsync()).StatusCode);
            var store = app.Services.GetRequiredService<CompanyStore>();
            var ownerSubject = _authority + "|sub-owner";
            var created = store.CreateCompany("Fictional Harbor", Guid.NewGuid().ToString("D"), ownerSubject);
            var second = new CompanyStore(
                Path.Combine(_root.Path, "host", "workspace.sqlite"),
                ownerSubject,
                CompanyWorkspace.Definitions(),
                companyId: created.Id
            );
            second.Apply(
                new CompanyCommand(Guid.NewGuid().ToString("D"), "member.invite")
                {
                    Body = "guest@example.invalid",
                    Decision = CompanyRoles.Viewer,
                },
                ownerSubject,
                out _
            );
            _email = "guest@example.invalid";
            var guest = new Browser(hostClient, idpClient);
            Assert.Equal("/", (await guest.SignInAsync()).Headers.Location!.OriginalString);
            var scope = CompanyHosting.BrowserScope(_authority + "|sub-guest");
            foreach (var selected in new string?[] { null, "co-does-not-exist" })
            {
                var response = await guest.Host(HttpMethod.Get, "/api/session", company: selected);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl!.NoStore);
                using var session = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
                Assert.True(session.RootElement.GetProperty("signedIn").GetBoolean());
                Assert.True(session.RootElement.GetProperty("companySelectionRequired").GetBoolean());
                Assert.Equal(scope, session.RootElement.GetProperty("browserScope").GetString());
                var choice = Assert.Single(session.RootElement.GetProperty("companies").EnumerateArray());
                Assert.Equal(created.Id, choice.GetProperty("id").GetString());
                Assert.Equal("Fictional Harbor", choice.GetProperty("name").GetString());
                Assert.Equal("viewer", choice.GetProperty("role").GetString());
                Assert.False(session.RootElement.TryGetProperty("agentModel", out _));
                Assert.False(session.RootElement.TryGetProperty("role", out _));
            }
            var directory = await guest.Host(
                HttpMethod.Get,
                "/api/companies",
                memberScope: scope,
                company: "co-does-not-exist"
            );
            Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
            Assert.True(directory.Headers.CacheControl!.NoStore);
            using (var choices = JsonDocument.Parse(await directory.Content.ReadAsStringAsync(Token)))
                Assert.Equal(
                    created.Id,
                    Assert.Single(choices.RootElement.EnumerateArray()).GetProperty("id").GetString()
                );
            Assert.Equal(
                HttpStatusCode.Conflict,
                (
                    await guest.Host(
                        HttpMethod.Get,
                        "/api/companies",
                        memberScope: CompanyHosting.BrowserScope(ownerSubject)
                    )
                ).StatusCode
            );
            Assert.Equal(HttpStatusCode.Forbidden, (await guest.Host(HttpMethod.Get, "/api/company")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await guest.Host(HttpMethod.Post, "/api/companies")).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await guest.Host(HttpMethod.Post, "/api/companies", company: created.Id)).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.OK,
                (await guest.Host(HttpMethod.Get, "/api/company", company: created.Id)).StatusCode
            );
            using (
                var selected = JsonDocument.Parse(
                    await (
                        await guest.Host(HttpMethod.Get, "/api/session", company: created.Id)
                    ).Content.ReadAsStringAsync(Token)
                )
            )
            {
                Assert.Equal("viewer", selected.RootElement.GetProperty("role").GetString());
                Assert.False(selected.RootElement.TryGetProperty("companySelectionRequired", out _));
            }

            var member = second.Members().Single(m => m.Id == _authority + "|sub-guest");
            second.Apply(
                new CompanyCommand(Guid.NewGuid().ToString("D"), "member.revoke")
                {
                    Id = member.Id,
                    ExpectedVersion = member.Version,
                },
                ownerSubject,
                out _
            );
            using (
                var revoked = JsonDocument.Parse(
                    await (
                        await guest.Host(HttpMethod.Get, "/api/session", company: created.Id)
                    ).Content.ReadAsStringAsync(Token)
                )
            )
            {
                Assert.True(revoked.RootElement.GetProperty("companySelectionRequired").GetBoolean());
                Assert.Empty(revoked.RootElement.GetProperty("companies").EnumerateArray());
            }
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await guest.Host(HttpMethod.Get, "/api/company", company: created.Id)).StatusCode
            );
            using (
                var choices = JsonDocument.Parse(
                    await (await guest.Host(HttpMethod.Get, "/api/companies")).Content.ReadAsStringAsync(Token)
                )
            )
                Assert.Empty(choices.RootElement.EnumerateArray());
            Assert.Equal(HttpStatusCode.OK, (await guest.Host(HttpMethod.Post, "/signout")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.Host(HttpMethod.Get, "/api/companies")).StatusCode);
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
