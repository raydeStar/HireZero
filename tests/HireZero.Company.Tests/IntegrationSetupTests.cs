using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.AgentApps;
using HireZero.Integrations.AzureDevOps;
using HireZero.Integrations.Google;
using HireZero.Integrations.OpenRouter;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

// Integration setup in the app (docs/adr/0038-integration-setup-in-the-app.md): settings merge into the file the
// integration reads and keep its other keys; values are checked by the integration's own rules; a pasted secret is
// stored encrypted, read last, never returned, and audited by name only. The secret below is fictional.
public sealed class IntegrationSetupTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private const string FictionalKey = "{\"type\":\"service_account\",\"private_key\":\"fictional-not-a-key\"}";
    private readonly TemporaryRoot _root = new("integration-setup-");

    public async ValueTask DisposeAsync()
    {
        SecretSource.UseAppSecrets(null);
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }

    [Fact]
    public void OnlyTheSecretsIntegrationsDeclareCanBePasted()
    {
        IConfigurableIntegration[] integrations =
        [
            new GoogleAnalyticsProvider(_root.Path),
            new HireZero.Integrations.SiteAnalytics.SiteAnalyticsProvider(_root.Path),
            new GoogleDriveDocuments(_root.Path, () => null),
            new GoogleAdsProvider(_root.Path),
            new AzureDevOpsToolProvider(_root.Path),
            AgentAppsToolProvider.Company(_root.Path),
            new OpenRouterToolProvider(_root.Path),
        ];
        Assert.Equal(
            LocalSecretStore.Names.Order(StringComparer.Ordinal),
            integrations
                .SelectMany(i => i.Setup.Secrets.Select(s => s.Name))
                .Concat(LocalSecretStore.SignedIn)
                .Order(StringComparer.Ordinal)
        );
        // A sign-in's token is written by the sign-in, never offered for pasting.
        Assert.DoesNotContain(
            integrations.SelectMany(i => i.Setup.Secrets),
            s => LocalSecretStore.SignedIn.Contains(s.Name)
        );
        Assert.DoesNotContain(CompanyKeyProtection.Variable, LocalSecretStore.Names);
        // Nothing that names a program to run is offered as a setting.
        Assert.DoesNotContain(integrations.SelectMany(i => i.Setup.Fields), f => f.Id is "az" or "steer");
    }

    [Fact]
    public async Task SettingsMergeAndSecretsAreWriteOnly()
    {
        var token = TestContext.Current.CancellationToken;
        var data = Path.Combine(_root.Path, "host");
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(
            Path.Combine(data, "google.json"),
            "{\"searchConsoleSite\":\"sc-domain:example.invalid\",\"note\":\"kept\"}",
            token
        );
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            data,
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { }
        )!;
        var store = app.Services.GetRequiredService<CompanyStore>();
        await app.StartAsync(token);
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(address) };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        async Task<HttpStatusCode> Put(string path, object body)
        {
            using var response = await client.PutAsJsonAsync(path, body, token);
            return response.StatusCode;
        }

        // Settings: checked by the integration's rule, merged into its file, other keys kept.
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            await Put(
                "/api/company/integrations/google-analytics/settings",
                new { values = new { ga4Property = "not-a-number" } }
            )
        );
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            await Put("/api/company/integrations/google-analytics/settings", new { values = new { az = "/bin/sh" } })
        );
        Assert.Equal(
            HttpStatusCode.OK,
            await Put(
                "/api/company/integrations/google-analytics/settings",
                new { values = new { ga4Property = "properties/123456789" } }
            )
        );
        var file = JsonDocument
            .Parse(await File.ReadAllTextAsync(Path.Combine(data, "google.json"), token))
            .RootElement;
        Assert.Equal(
            ("properties/123456789", "sc-domain:example.invalid", "kept"),
            (
                file.GetProperty("ga4Property").GetString(),
                file.GetProperty("searchConsoleSite").GetString(),
                file.GetProperty("note").GetString()
            )
        );
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            await Put(
                "/api/company/integrations/agent-apps/settings",
                new { values = new { apps = new[] { "codex", "other" } } }
            )
        );

        // Secrets: only a declared name, stored encrypted, read last, never returned.
        Assert.Equal(
            HttpStatusCode.NotFound,
            await Put(
                "/api/company/integrations/google-analytics/secrets",
                new { name = CompanyKeyProtection.Variable, value = "x" }
            )
        );
        Assert.Equal(
            HttpStatusCode.OK,
            await Put(
                "/api/company/integrations/google-analytics/secrets",
                new { name = GoogleAnalyticsProvider.KeySecret, value = FictionalKey }
            )
        );
        var stored = await File.ReadAllTextAsync(
            Path.Combine(data, "secrets", GoogleAnalyticsProvider.KeySecret + ".secret"),
            token
        );
        Assert.DoesNotContain("fictional-not-a-key", stored);
        var setup = await client.GetStringAsync("/api/company/integrations/setup", token);
        Assert.DoesNotContain("fictional-not-a-key", setup);
        if (Environment.GetEnvironmentVariable(GoogleAnalyticsProvider.KeySecret) is null)
        {
            Assert.Contains("\"origin\":\"app\"", setup);
            var secrets = new LocalSecretStore(
                data,
                app.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
                LocalSecretStore.Names
            );
            Assert.Equal(FictionalKey, secrets.Read(GoogleAnalyticsProvider.KeySecret));
            Assert.NotEqual(FictionalKey, SecretSource.Default.Read(GoogleAnalyticsProvider.KeySecret));
        }
        var events = store.Feed(0, 500).Where(e => e.Type.StartsWith("integration.", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Text == "Secret set: " + GoogleAnalyticsProvider.KeySecret + ".");
        Assert.DoesNotContain(events, e => e.Text.Contains("fictional-not-a-key", StringComparison.Ordinal));

        Assert.Equal(
            HttpStatusCode.OK,
            await Put(
                "/api/company/integrations/google-analytics/secrets",
                new { name = GoogleAnalyticsProvider.KeySecret, remove = true }
            )
        );
        Assert.False(File.Exists(Path.Combine(data, "secrets", GoogleAnalyticsProvider.KeySecret + ".secret")));

        // An integration set up while HireZero runs starts working without a restart (docs/adr/0039).
        var tools = app.Services.GetRequiredService<ToolRegistry>();
        Assert.False(tools.Has("openrouter.status"));
        Assert.Equal(
            HttpStatusCode.OK,
            await Put(
                "/api/company/integrations/openrouter/settings",
                new { values = new { model = "fictional-vendor/fictional-model" } }
            )
        );
        Assert.Equal(
            HttpStatusCode.OK,
            await Put(
                "/api/company/integrations/openrouter/secrets",
                new { name = "HIREZERO_OPENROUTER_API_KEY", value = "fictional-openrouter-key" }
            )
        );
        Assert.True(tools.Has("openrouter.status"));
        await app.StopAsync(token);
    }
}
