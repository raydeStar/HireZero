using System.Net;
using System.Text.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

public sealed partial class HostedSignInTests
{
    private async Task<WebApplication> StartInvitationHost(bool enabled = true, bool signup = true)
    {
        await StartProviderAsync();
        var hosted = new HostedOptions(
            new Uri("https://" + Public + "/"),
            new Uri("http://127.0.0.1:0"),
            _authority,
            ClientId,
            TrustedProxy: IPAddress.Loopback
        ).Validated();
        var settings = CompanySettings.Load(n =>
            n == "HIREZERO_OPEN_SIGNUP" && signup || n == "HIREZERO_SIGNUP_INVITES" && enabled ? "1" : null
        );
        var app = CompanyWorkspace.TryCreate(
            [CompanyHosting.HostedFlag],
            "owner@example.invalid",
            false,
            Path.Combine(_root.Path, "invite-host"),
            new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            fixtureHosted: hosted,
            settings: settings
        )!;
        await app.StartAsync(Token);
        return app;
    }

    private static HttpClient InvitationClient(WebApplication app) =>
        new(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
            }
        )
        {
            BaseAddress = new Uri(
                app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()
            ),
        };

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Token);
        Assert.True(response.IsSuccessStatusCode, $"HTTP {response.StatusCode}: {text}");
        using var parsed = JsonDocument.Parse(text);
        return parsed.RootElement.Clone();
    }

    private async Task<(string Company, string Token, string Scope)> IssueInvitation(
        Browser owner,
        string email = "guest@example.invalid"
    )
    {
        _email = "founder@example.invalid";
        Assert.Equal(HttpStatusCode.Redirect, (await owner.SignInAsync()).StatusCode);
        var company = (await Json(await owner.Host(HttpMethod.Post, "/api/signup"))).GetProperty("id").GetString()!;
        var scope = (await Json(await owner.Host(HttpMethod.Get, "/api/session", company: company)))
            .GetProperty("browserScope")
            .GetString()!;
        var issued = await Json(
            await owner.Host(
                HttpMethod.Post,
                "/api/company/member-invites",
                company: company,
                memberScope: scope,
                body: CompanyJson.Encode(new { email, role = "viewer" })
            )
        );
        var link = new Uri(issued.GetProperty("link").GetString()!);
        Assert.Equal("/join", link.AbsolutePath);
        Assert.Equal("", link.Query);
        Assert.Equal(Public, link.Host);
        return (company, link.Fragment[1..], scope);
    }

    [Fact]
    public async Task InvitationRequiresExplicitProtectedConsentAfterVerifiedOidcAndSupportsOwnCompanyThenLeave()
    {
        await using var app = await StartInvitationHost();
        using var host = InvitationClient(app);
        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        var owner = new Browser(host, idp);
        var issued = await IssueInvitation(owner);
        var guest = new Browser(host, idp);
        var prepare = CompanyJson.Encode(new { token = issued.Token });
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await guest.Host(HttpMethod.Post, "/api/join/prepare", body: prepare, csrf: false)).StatusCode
        );
        var prepared = await guest.Host(HttpMethod.Post, "/api/join/prepare", body: prepare);
        Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
        var cookie = Assert.Single(prepared.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-hirezero-join=", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.Contains("max-age=900", cookie);
        Assert.DoesNotContain(issued.Token, cookie);
        Assert.Equal("no-store", prepared.Headers.CacheControl!.ToString());
        var anonymous = await Json(await guest.Host(HttpMethod.Get, "/api/join"));
        Assert.False(anonymous.GetProperty("signedIn").GetBoolean());
        Assert.DoesNotContain("guest@example.invalid", anonymous.ToString());
        _email = "guest@example.invalid";
        Assert.Equal("/join", (await guest.SignInAsync("/signin?join=1")).Headers.Location!.OriginalString);
        // Neither the landing page, a GET of the accept route, nor the login callback can join.
        await guest.Host(HttpMethod.Get, "/join");
        await guest.Host(HttpMethod.Get, "/api/join/accept");
        Assert.Empty((await Json(await guest.Host(HttpMethod.Get, "/api/companies"))).EnumerateArray());
        var preview = await Json(await guest.Host(HttpMethod.Get, "/api/join"));
        Assert.True(preview.GetProperty("matches").GetBoolean());
        var scope = preview.GetProperty("browserScope").GetString();
        var consent = CompanyJson.Encode(
            new
            {
                invitation = preview.GetProperty("invitation").GetString(),
                expectedVersion = preview.GetProperty("version").GetInt32(),
            }
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await guest.Host(HttpMethod.Post, "/api/join/accept", body: consent, memberScope: scope, csrf: false)
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await guest.Host(HttpMethod.Post, "/api/join/accept", body: consent)).StatusCode
        );
        Assert.Equal(
            issued.Company,
            (await Json(await guest.Host(HttpMethod.Post, "/api/join/accept", body: consent, memberScope: scope)))
                .GetProperty("id")
                .GetString()
        );
        Assert.Equal(HttpStatusCode.Gone, (await guest.Host(HttpMethod.Get, "/api/join")).StatusCode);
        var session = await Json(await guest.Host(HttpMethod.Get, "/api/session", company: issued.Company));
        Assert.True(session.GetProperty("openSignup").GetBoolean());
        Assert.Equal("viewer", session.GetProperty("role").GetString());
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await guest.Host(
                    HttpMethod.Post,
                    "/api/company/member-invites",
                    company: issued.Company,
                    body: "{\"email\":\"third@example.invalid\",\"role\":\"viewer\"}"
                )
            ).StatusCode
        );
        var own = await Json(
            await guest.Host(HttpMethod.Post, "/api/signup", memberScope: scope, company: issued.Company)
        );
        Assert.NotEqual(issued.Company, own.GetProperty("id").GetString());
        Assert.False(
            (await Json(await guest.Host(HttpMethod.Get, "/api/session", company: issued.Company)))
                .GetProperty("openSignup")
                .GetBoolean()
        );
        var members = await Json(await guest.Host(HttpMethod.Get, "/api/company/members", company: issued.Company));
        var member = members
            .EnumerateArray()
            .Single(m => m.GetProperty("email").GetString() == "guest@example.invalid");
        var leave = CompanyJson.Encode(
            new
            {
                action = "member.leave",
                requestId = Guid.NewGuid().ToString("D"),
                expectedVersion = member.GetProperty("version").GetInt32(),
            }
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await guest.Host(
                    HttpMethod.Post,
                    "/api/company/commands",
                    company: issued.Company,
                    memberScope: scope,
                    body: leave
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await guest.Host(HttpMethod.Get, "/api/company", company: issued.Company)).StatusCode
        );
        Assert.Single((await Json(await guest.Host(HttpMethod.Get, "/api/companies"))).EnumerateArray());
        Assert.Equal(
            HttpStatusCode.Gone,
            (await guest.Host(HttpMethod.Post, "/api/join/prepare", body: prepare)).StatusCode
        );
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task WrongAccountAndAnotherTabsInvitationAreRefusedWithoutConsumingEitherLink()
    {
        await using var app = await StartInvitationHost();
        using var host = InvitationClient(app);
        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        var owner = new Browser(host, idp);
        var issued = await IssueInvitation(owner);
        var guest = new Browser(host, idp);
        await guest.Host(HttpMethod.Post, "/api/join/prepare", body: CompanyJson.Encode(new { token = issued.Token }));
        _email = "wrong@example.invalid";
        await guest.SignInAsync("/signin?join=1");
        var wrong = await Json(await guest.Host(HttpMethod.Get, "/api/join"));
        Assert.False(wrong.GetProperty("matches").GetBoolean());
        var consent = CompanyJson.Encode(
            new { invitation = wrong.GetProperty("invitation").GetString(), expectedVersion = 1 }
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await guest.Host(
                    HttpMethod.Post,
                    "/api/join/accept",
                    memberScope: wrong.GetProperty("browserScope").GetString(),
                    body: consent
                )
            ).StatusCode
        );
        _email = "guest@example.invalid";
        var accountChallenge = await guest.Host(HttpMethod.Get, "/signin?join=1&account=1");
        Assert.Contains("prompt=select_account", accountChallenge.Headers.Location!.Query);
        await guest.SignInAsync("/signin?join=1&account=1");
        var matching = await Json(await guest.Host(HttpMethod.Get, "/api/join"));
        Assert.True(matching.GetProperty("matches").GetBoolean());
        var replacement = await Json(
            await owner.Host(
                HttpMethod.Post,
                "/api/company/member-invites",
                company: issued.Company,
                memberScope: issued.Scope,
                body: "{\"email\":\"guest@example.invalid\",\"role\":\"approver\",\"expectedVersion\":1}"
            )
        );
        var token = new Uri(replacement.GetProperty("link").GetString()!).Fragment[1..];
        await guest.Host(HttpMethod.Post, "/api/join/prepare", body: CompanyJson.Encode(new { token }));
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await guest.Host(
                    HttpMethod.Post,
                    "/api/join/accept",
                    memberScope: matching.GetProperty("browserScope").GetString(),
                    body: consent
                )
            ).StatusCode
        );
        Assert.Empty((await Json(await guest.Host(HttpMethod.Get, "/api/companies"))).EnumerateArray());
        Assert.Equal(
            "approver",
            (await Json(await guest.Host(HttpMethod.Get, "/api/join"))).GetProperty("role").GetString()
        );
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task InvitationsAreOffByDefaultAndTheJoinBoundaryIsRateLimited()
    {
        await using var app = await StartInvitationHost(enabled: false);
        using var host = InvitationClient(app);
        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        var guest = new Browser(host, idp);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await guest.Host(HttpMethod.Post, "/api/join/prepare", body: "{\"token\":\"invalid\"}")).StatusCode
        );
        HttpResponseMessage? response = null;
        for (var i = 0; i < 31; i++)
            response = await guest.Host(HttpMethod.Get, "/api/join");
        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), response.Headers.RetryAfter!.Delta);
        Assert.Equal(HttpStatusCode.OK, (await guest.Host(HttpMethod.Get, "/api/session")).StatusCode);
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task InvitationRateLimitCoversTrailingSlashRoutesAndKeepsSecurityHeaders()
    {
        await using var app = await StartInvitationHost();
        using var host = InvitationClient(app);
        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        var owner = new Browser(host, idp);
        var issued = await IssueInvitation(owner);
        // Issuance used one permit. A different spelling must not create a side entrance.
        for (var i = 0; i < 29; i++)
            Assert.Equal(
                HttpStatusCode.UnprocessableEntity,
                (
                    await owner.Host(
                        HttpMethod.Post,
                        "/api/company/member-invites/",
                        company: issued.Company,
                        memberScope: issued.Scope,
                        body: "{\"email\":\"another@example.invalid\",\"role\":\"owner\"}"
                    )
                ).StatusCode
            );
        var blocked = await owner.Host(
            HttpMethod.Post,
            "/api/company/member-invites/",
            company: issued.Company,
            memberScope: issued.Scope,
            body: "{\"email\":\"another@example.invalid\",\"role\":\"viewer\"}"
        );
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("no-store", blocked.Headers.CacheControl!.ToString());
        Assert.Equal(
            LocalHosting.ContentSecurityPolicy,
            Assert.Single(blocked.Headers.GetValues("Content-Security-Policy"))
        );
        Assert.Equal(HttpStatusCode.TooManyRequests, (await owner.Host(HttpMethod.Get, "/join/")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.Host(HttpMethod.Get, "/api/session", company: issued.Company)).StatusCode
        );
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task OnlyTheExplicitInvitationRoutesBypassCompanyMembership()
    {
        await using var app = await StartInvitationHost();
        using var host = InvitationClient(app);
        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        var owner = new Browser(host, idp);
        var issued = await IssueInvitation(owner);
        var guest = new Browser(host, idp);
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await guest.Host(
                    HttpMethod.Post,
                    "/api/join/prepare/",
                    body: CompanyJson.Encode(new { token = issued.Token })
                )
            ).StatusCode
        );
        Assert.False((await Json(await guest.Host(HttpMethod.Get, "/API/JOIN/"))).GetProperty("signedIn").GetBoolean());
        _email = "guest@example.invalid";
        await guest.SignInAsync("/signin?join=1");
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.Host(HttpMethod.Get, "/api/join/unrelated")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.Host(HttpMethod.Post, "/api/join")).StatusCode);
        Assert.Empty((await Json(await guest.Host(HttpMethod.Get, "/api/companies"))).EnumerateArray());
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task InvitationCanEstablishVerifiedEntryWithEnrollmentClosedButNeverWithoutItsCookie()
    {
        await using var app = await StartInvitationHost(signup: false);
        var root = app.Services.GetRequiredService<CompanyStore>();
        root.ConfigureSignup(new());
        var created = root.Signup("founder", "founder@example.invalid", true, "Founder", new());
        var free = new CompanyStore(root.Database, "founder", CompanyWorkspace.Definitions(), companyId: created.Id)
        {
            SignupInvitesEnabled = true,
        };
        var invite = free.InviteSignupMember("founder", "guest@example.invalid", "viewer");
        using var host = InvitationClient(app);
        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        var guest = new Browser(host, idp);
        _email = "guest@example.invalid";
        Assert.Equal("/?signin=not-invited", (await guest.SignInAsync()).Headers.Location!.OriginalString);
        await guest.Host(HttpMethod.Post, "/api/join/prepare", body: CompanyJson.Encode(new { token = invite.Token }));
        _verified = false;
        Assert.Equal(
            "/?signin=not-invited",
            (await guest.SignInAsync("/signin?join=1")).Headers.Location!.OriginalString
        );
        _verified = true;
        Assert.Equal("/join", (await guest.SignInAsync("/signin?join=1")).Headers.Location!.OriginalString);
        Assert.Throws<WorkspaceException>(() => free.Actor(_authority + "|sub-guest"));
        var preview = await Json(await guest.Host(HttpMethod.Get, "/api/join"));
        var scope = preview.GetProperty("browserScope").GetString();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await guest.Host(
                    HttpMethod.Post,
                    "/api/join/accept",
                    memberScope: scope,
                    body: CompanyJson.Encode(
                        new { invitation = preview.GetProperty("invitation").GetString(), expectedVersion = 1 }
                    )
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await guest.Host(HttpMethod.Post, "/api/signup", company: created.Id, memberScope: scope)).StatusCode
        );
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task TamperedAndExpiredInvitationCookiesAreRejectedBeforePreview()
    {
        await using var app = await StartInvitationHost();
        using var host = InvitationClient(app);
        var protector = app
            .Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("hirezero-company-invitation-v1")
            .ToTimeLimitedDataProtector();
        foreach (
            var cookie in new[]
            {
                "tampered",
                protector.Protect(new string('a', 64), DateTimeOffset.UtcNow.AddMinutes(-1)),
            }
        )
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/join");
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-Host", Public);
            request.Headers.Add("Cookie", "__Host-hirezero-join=" + cookie);
            var response = await host.SendAsync(request, Token);
            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            Assert.Contains("missing or expired", await response.Content.ReadAsStringAsync(Token));
        }
        await app.StopAsync(Token);
    }
}
