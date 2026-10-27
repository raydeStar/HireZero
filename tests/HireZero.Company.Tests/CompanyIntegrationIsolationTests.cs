using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Google;
using HireZero.Integrations.Microsoft365;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed class CompanyIntegrationIsolationTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-connections-");
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CompanyIntegrationScope Scope(string company, bool local = true, HttpMessageHandler? google = null)
    {
        var store = new CompanyStore(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            companyId: company
        );
        return new(
            _root.Path,
            store,
            new ToolRegistry(store),
            local,
            _protection,
            "http://127.0.0.1:5065",
            googleHandler: google
        );
    }

    [Fact]
    public void CredentialsAndEncryptionPurposesBelongToOneCompany()
    {
        var a = Scope("co-a");
        var b = Scope("co-b");
        Assert.NotEqual(a.DataRoot, b.DataRoot);
        Assert.False(a.MachineAccess);
        a.PastedSecrets!.Write("HIREZERO_OPENROUTER_API_KEY", "fictional-a-key");
        File.WriteAllText(Path.Combine(a.DataRoot, "openrouter.json"), "{\"model\":\"fictional/model-a\"}");
        a.Activate();
        Assert.True(a.Tools.Has("openrouter.status"));
        Assert.False(b.Tools.Has("openrouter.status"));
        Assert.False(b.OpenRouter.Status().Active);
        Assert.Null(b.Secrets.Read("HIREZERO_OPENROUTER_API_KEY"));
        Assert.Equal("fictional-a-key", Scope("co-a").Secrets.Read("HIREZERO_OPENROUTER_API_KEY"));
        Directory.CreateDirectory(Path.Combine(b.DataRoot, "secrets"));
        File.Copy(
            Path.Combine(a.DataRoot, "secrets", "HIREZERO_OPENROUTER_API_KEY.secret"),
            Path.Combine(b.DataRoot, "secrets", "HIREZERO_OPENROUTER_API_KEY.secret")
        );
        Assert.Null(b.Secrets.Read("HIREZERO_OPENROUTER_API_KEY"));
        Assert.DoesNotContain(a.Configurable, i => i.Setup.Id is "agent-apps" or "azure-devops");
        Assert.All(
            a.Statuses().Where(s => s.Id is "agent-apps" or "azure-devops" or "jev"),
            s => Assert.False(s.Active)
        );
    }

    [Fact]
    public void HostedCompaniesReadOnlyTheirOwnDeliveredCredentials()
    {
        var a = Scope("local-company", local: false);
        var b = Scope("co-hosted-b", local: false);
        Assert.NotEqual(_root.Path, a.DataRoot);
        Assert.Null(a.PastedSecrets);
        Assert.False(a.MachineAccess);
        Directory.CreateDirectory(Path.Combine(a.DataRoot, "credentials"));
        File.WriteAllText(
            Path.Combine(a.DataRoot, "credentials", "HIREZERO_OPENROUTER_API_KEY"),
            "fictional-delivered-key"
        );
        Assert.Equal("fictional-delivered-key", a.Secrets.Read("HIREZERO_OPENROUTER_API_KEY"));
        Assert.Null(b.Secrets.Read("HIREZERO_OPENROUTER_API_KEY"));
        Assert.Equal(SecretOrigin.Credential, a.Secrets.Describe("HIREZERO_OPENROUTER_API_KEY").Origin);
        Assert.Equal(SecretOrigin.Missing, b.Secrets.Describe("HIREZERO_OPENROUTER_API_KEY").Origin);
    }

    [Fact]
    public async Task ProviderInstancesUseTheirOwnTokensAndRevocationStopsCachedTokens()
    {
        using var handler = new Tokens();
        var a = Scope("co-token-a", google: handler);
        var b = Scope("co-token-b", google: handler);
        foreach (var scope in new[] { a, b })
        {
            Directory.CreateDirectory(scope.DataRoot);
            File.WriteAllText(
                Path.Combine(scope.DataRoot, "google.json"),
                "{\"oauthClientId\":\"fictional.apps.googleusercontent.com\",\"ga4Property\":\"123456\"}"
            );
            scope.PastedSecrets!.Write(GoogleOAuth.ClientSecretName, "fictional-client-secret");
            scope.PastedSecrets.Write(GoogleOAuth.RefreshTokenName, scope.Store.CompanyId);
        }
        Assert.Equal("co-token-a", (await a.Google.OAuth.AuthorizeAsync(Token)).Parameter);
        Assert.Equal("co-token-b", (await b.Google.OAuth.AuthorizeAsync(Token)).Parameter);
        a.Change(() => a.PastedSecrets!.Remove(GoogleOAuth.RefreshTokenName));
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.Google.OAuth.AuthorizeAsync(Token));
        Assert.Equal("co-token-b", (await b.Google.OAuth.AuthorizeAsync(Token)).Parameter);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task SignInBindsCompanyOwnerAndVersionAndConsumesEveryOutcomeOnce()
    {
        var a = Scope("co-consent-a");
        var b = Scope("co-consent-b");
        var pending = new CompanyConnectionSignIns("google");
        var calls = 0;
        Task Save(CompanyConnectionSignIns.Ticket sign, string code, CancellationToken token)
        {
            sign.DemandCurrent();
            sign.Scope.PastedSecrets!.Write(GoogleOAuth.RefreshTokenName, "fictional-refresh");
            calls++;
            return Task.CompletedTask;
        }
        pending.Begin(b, Owner, () => ("https://example.invalid/authorize", "state-b", "verifier-b"));
        var result = await pending.CompleteAsync("state-b", "code", null, Save, Token);
        Assert.Contains("company=co-consent-b&google=connected", result);
        Assert.Null(a.PastedSecrets!.Read(GoogleOAuth.RefreshTokenName));
        Assert.Equal("fictional-refresh", b.PastedSecrets!.Read(GoogleOAuth.RefreshTokenName));
        Assert.EndsWith("expired", await pending.CompleteAsync("state-b", "code", null, Save, Token));
        pending.Begin(a, Owner, () => ("url", "state-a", "verifier"));
        a.Change(() => a.PastedSecrets.Remove(GoogleOAuth.RefreshTokenName));
        Assert.EndsWith("failed", await pending.CompleteAsync("state-a", "code", null, Save, Token));
        pending.Begin(a, Owner, () => ("url", "cancelled-a", "verifier"));
        Assert.EndsWith(
            "cancelled",
            await pending.CompleteAsync("cancelled-a", null, "private-provider-detail", Save, Token)
        );
        Assert.EndsWith("expired", await pending.CompleteAsync("cancelled-a", "code", null, Save, Token));
        pending.Begin(a, Owner, () => ("url", "old-a", "verifier"));
        pending.Begin(a, Owner, () => ("url", "new-a", "verifier"));
        Assert.EndsWith("expired", await pending.CompleteAsync("old-a", "code", null, Save, Token));
        Assert.Throws<WorkspaceException>(() =>
            pending.Begin(a, "stranger@example.invalid", () => ("url", "stranger", "verifier"))
        );
        Assert.Equal(1, calls);
    }

    [Fact]
    public void MicrosoftRefreshCannotResurrectADisconnectedAccount()
    {
        var a = Scope("co-microsoft-a");
        var b = Scope("co-microsoft-b");
        var account = new MicrosoftAccount(
            "owner@example.invalid",
            "fictional-id",
            true,
            "protected-refresh",
            "protected-access",
            null,
            ["Mail.Read"],
            null,
            null,
            null,
            null,
            null,
            "2026-10-25T00:00:00Z"
        );
        a.MicrosoftStore.SaveAccount(account);
        Assert.Null(b.MicrosoftStore.Account());
        a.MicrosoftStore.Disconnect();
        Assert.False(a.MicrosoftStore.UpdateAccount(account, account with { ProtectedAccessToken = "late-token" }));
        Assert.Null(a.MicrosoftStore.Account());
    }

    [Fact]
    public async Task HttpConfigurationActivationAndCallbacksStayInTheSelectedCompany()
    {
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { }
        )!;
        await app.StartAsync(Token);
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = new(address),
        };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        using var created = await client.PostAsJsonAsync(
            "/api/companies",
            new { name = "Fictional second studio" },
            Token
        );
        created.EnsureSuccessStatusCode();
        var company = (await created.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetString()!;
        async Task Put(string path, object body)
        {
            using var response = await client.PutAsJsonAsync(path, body, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await Put(
            "/api/company/integrations/openrouter/settings",
            new { values = new { model = "fictional/model-a" } }
        );
        await Put(
            "/api/company/integrations/openrouter/secrets",
            new { name = "HIREZERO_OPENROUTER_API_KEY", value = "fictional-a-key" }
        );
        Assert.True(app.Services.GetRequiredService<ToolRegistry>().Has("openrouter.status"));
        client.DefaultRequestHeaders.Add("X-HireZero-Company", company);
        var setups = await client.GetFromJsonAsync<JsonElement[]>("/api/company/integrations/setup", Token);
        Assert.All(setups!, s => Assert.Equal(company, s.GetProperty("companyId").GetString()));
        var openRouter = Assert.Single(setups!, s => s.GetProperty("id").GetString() == "openrouter");
        Assert.False(openRouter.GetProperty("active").GetBoolean());
        Assert.Equal("missing", openRouter.GetProperty("secrets")[0].GetProperty("origin").GetString());
        Assert.Null(openRouter.GetProperty("fields")[0].GetProperty("value").GetString());
        var snapshot = await client.GetFromJsonAsync<JsonElement>("/api/company", Token);
        Assert.DoesNotContain(
            snapshot.GetProperty("capabilities").GetProperty("tools").EnumerateArray(),
            t => t.GetProperty("id").GetString() == "openrouter.status"
        );
        using var refused = await client.PostAsJsonAsync("/api/company/github/sign-in", new { }, Token);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        await Put(
            "/api/company/integrations/google-analytics/settings",
            new { values = new { oauthClientId = "fictional.apps.googleusercontent.com" } }
        );
        await Put(
            "/api/company/integrations/google-analytics/secrets",
            new { name = GoogleOAuth.ClientSecretName, value = "fictional-google-secret" }
        );
        using var start = await client.PostAsJsonAsync("/api/connections/google/start", new { }, Token);
        start.EnsureSuccessStatusCode();
        var url = (await start.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("url").GetString()!;
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
        // The callback arrives without a company header; it must still return to the initiating company.
        client.DefaultRequestHeaders.Remove("X-HireZero-Company");
        using var callback = await client.GetAsync(
            "/oauth/google/callback?state=" + Uri.EscapeDataString(state) + "&error=access_denied",
            Token
        );
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("company=" + company + "&google=cancelled", callback.Headers.Location!.OriginalString);
        var firstGoogle = await client.GetFromJsonAsync<JsonElement>("/api/connections/google", Token);
        Assert.False(firstGoogle.GetProperty("configured").GetBoolean());
        client.DefaultRequestHeaders.Add("X-HireZero-Company", company);
        await Put("/api/connections/microsoft", new { clientId = "11111111-1111-1111-1111-111111111111" });
        Assert.True(
            (await client.GetFromJsonAsync<JsonElement>("/api/connections/microsoft", Token))
                .GetProperty("configured")
                .GetBoolean()
        );
        client.DefaultRequestHeaders.Remove("X-HireZero-Company");
        Assert.False(
            (await client.GetFromJsonAsync<JsonElement>("/api/connections/microsoft", Token))
                .GetProperty("configured")
                .GetBoolean()
        );
        await app.StopAsync(Token);
    }

    private sealed class Tokens : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Assert.Equal("oauth2.googleapis.com", request.RequestUri!.Host);
            Calls++;
            var form = await request.Content!.ReadAsStringAsync(token);
            var refresh = form.Split('&')
                .Single(p => p.StartsWith("refresh_token=", StringComparison.Ordinal))
                .Split('=')[1];
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { access_token = Uri.UnescapeDataString(refresh), expires_in = 3600 }),
            };
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
