using System.Net;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Google;
using HireZero.Tools.Advertising;

// Google Ads behind the ads.* contracts (docs/adr/0032-google-ads-provider.md), against a fake handler with fictional
// API responses: a refresh-token sign-in at Google's token endpoint only, read queries it composes itself with the
// developer token and manager headers, money converted from micros, and refusals before anything is sent.
public sealed class GoogleAdsProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "google-ads-" + Guid.NewGuid().ToString("N"));

    public GoogleAdsProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string> Secrets = new()
    {
        [GoogleAdsProvider.DeveloperTokenSecret] = "fictional-developer-token",
        [GoogleAdsProvider.ClientSecretSecret] = "fictional-client-secret",
        [GoogleAdsProvider.RefreshTokenSecret] = "fictional-refresh-token",
    };

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Url, Dictionary<string, string> Headers, string Body)> Requests = [];
        public HttpStatusCode SearchStatus = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            var url = request.RequestUri!.ToString();
            var body = await request.Content!.ReadAsStringAsync(token);
            Requests.Add((url, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)), body));
            string json;
            var status = HttpStatusCode.OK;
            if (url == GoogleServiceAccount.TokenEndpoint)
                json = """{"access_token":"fictional-access","expires_in":3600}""";
            else if (SearchStatus != HttpStatusCode.OK)
            {
                status = SearchStatus;
                json = """{"error":{"code":403,"message":"fictional","status":"PERMISSION_DENIED"}}""";
            }
            else if (url.EndsWith(":mutate", StringComparison.Ordinal))
                json = """{"results":[{"resourceName":"customers/1234567890/fictional/111"}]}""";
            else if (body.Contains("WHERE campaign.id = ", StringComparison.Ordinal))
                json = """
                    {"results":[{"campaign":{"id":"111","name":"Fictional search","status":"ENABLED"},
                      "campaignBudget":{"id":"7701","amountMicros":"50000000","explicitlyShared":false},
                      "customer":{"currencyCode":"EUR"}}]}
                    """;
            else if (body.Contains("FROM campaign", StringComparison.Ordinal))
                json = """
                    {"results":[
                      {"campaign":{"resourceName":"customers/1234567890/campaigns/111","id":"111","name":"Fictional search","status":"ENABLED"},
                       "metrics":{"impressions":"5000","clicks":"250","costMicros":"312500000","conversions":12.5,"conversionsValue":1500.0}},
                      {"campaign":{"resourceName":"customers/1234567890/campaigns/222","id":"222","name":"Fictional video","status":"PAUSED"},
                       "metrics":{"impressions":"9000","clicks":"40","costMicros":"20000000"}}]}
                    """;
            else if (body.Contains("segments.date,", StringComparison.Ordinal))
                json = """
                    {"results":[
                      {"segments":{"date":"2026-03-01"},"metrics":{"impressions":"7000","clicks":"150","costMicros":"170000000","conversions":6.0,"conversionsValue":700.0}},
                      {"segments":{"date":"2026-03-02"},"metrics":{"impressions":"7000","clicks":"140","costMicros":"162500000","conversions":6.5,"conversionsValue":800.0}}]}
                    """;
            else
                json = """
                    {"results":[{"customer":{"resourceName":"customers/1234567890","currencyCode":"EUR"},
                      "metrics":{"impressions":"14000","clicks":"290","costMicros":"332500000","conversions":12.5,"conversionsValue":1500.0}}]}
                    """;
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly EmployeeManifest Manifest = new(
        "ads-test",
        "Ads test",
        "Fictional manifest declaring the ads tools.",
        "1.0.0",
        "Fictional instructions.",
        [new("chat", "Conversation", "Fictional.")],
        new("Fictional", "Ads tester", "Fictional.", [new("purpose", "Fictional purpose.")], "Fictional.")
    )
    {
        Tools =
        [
            new(AdsTools.Status.Id, ToolRisk.Read, "Status."),
            new(AdsTools.Performance.Id, ToolRisk.Read, "Performance."),
            new(AdsTools.Campaign.Id, ToolRisk.Read, "Campaign."),
            new(AdsTools.Change.Id, ToolRisk.External, "Change."),
        ],
    };

    private (IToolbox Tools, Fake Fake, GoogleAdsProvider Provider) Create(Dictionary<string, string>? secrets = null)
    {
        var fake = new Fake();
        var values = secrets ?? Secrets;
        var provider = new GoogleAdsProvider(_root, name => values.GetValueOrDefault(name), fake);
        var registry = new ToolRegistry(new MemoryToolAudit());
        provider.Register(registry);
        return (registry.For(Manifest), fake, provider);
    }

    private void Configure(object settings) =>
        File.WriteAllText(Path.Combine(_root, "google-ads.json"), JsonSerializer.Serialize(settings));

    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End = new(2026, 3, 2);

    [Fact]
    public async Task NothingIsSentUntilTheAccountClientAndSecretsAreSet()
    {
        var (tools, fake, provider) = Create();
        Assert.Contains("customerId", provider.Status().Reason);
        Configure(new { customerId = "123-456-7890" });
        Assert.Contains("clientId", provider.Status().Reason);
        Configure(
            new
            {
                customerId = "123-456-7890",
                clientId = "fictional.apps.example.invalid",
                apiVersion = "latest",
            }
        );
        Assert.Contains("apiVersion", provider.Status().Reason);
        Configure(new { customerId = "123-456-7890", clientId = "fictional.apps.example.invalid" });
        var partial = Create(new() { [GoogleAdsProvider.DeveloperTokenSecret] = "fictional" });
        Assert.Contains(GoogleAdsProvider.RefreshTokenSecret, partial.Provider.Status().Reason);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            partial.Tools.InvokeAsync(AdsTools.Performance, new(Start, End, "campaign", 10), Token)
        );
        Assert.True(provider.Status().Active);
        Assert.Empty(fake.Requests);
        Assert.Empty(partial.Fake.Requests);
    }

    [Fact]
    public async Task CampaignsAreReadWithReadQueriesTheProviderComposes()
    {
        var (tools, fake, _) = Create();
        Configure(
            new
            {
                customerId = "123-456-7890",
                loginCustomerId = "999-888-7777",
                clientId = "fictional.apps.example.invalid",
            }
        );

        var report = await tools.InvokeAsync(AdsTools.Performance, new(Start, End, "campaign", 10), Token);

        Assert.Equal(("Google Ads account 123-456-7890", "EUR"), (report.Source, report.Currency));
        Assert.Equal(new AdsRow("total", null, null, 14000, 290, 332.5m, 12.5, 1500m), report.Totals);
        Assert.Equal(new AdsRow("111", "Fictional search", "enabled", 5000, 250, 312.5m, 12.5, 1500m), report.Rows[0]);
        Assert.Equal(("paused", 20m, 0.0), (report.Rows[1].Status, report.Rows[1].Spend, report.Rows[1].Conversions));

        var signIn = fake.Requests[0];
        Assert.Equal(GoogleServiceAccount.TokenEndpoint, signIn.Url);
        Assert.Contains("grant_type=refresh_token", signIn.Body);
        Assert.Contains("refresh_token=fictional-refresh-token", signIn.Body);
        foreach (var (url, headers, body) in fake.Requests.Skip(1))
        {
            Assert.Equal(
                $"https://googleads.googleapis.com/{GoogleAdsProvider.DefaultApiVersion}/customers/1234567890/googleAds:search",
                url
            );
            Assert.Equal("fictional-developer-token", headers["developer-token"]);
            Assert.Equal("9998887777", headers["login-customer-id"]);
            Assert.Equal("Bearer fictional-access", headers["Authorization"]);
            var query = JsonDocument.Parse(body).RootElement.GetProperty("query").GetString()!;
            Assert.StartsWith("SELECT ", query, StringComparison.Ordinal);
            Assert.Contains("segments.date BETWEEN '2026-03-01' AND '2026-03-02'", query);
        }
        Assert.Contains("LIMIT 10", fake.Requests[^1].Body);
        Assert.Single(fake.Requests, r => r.Url == GoogleServiceAccount.TokenEndpoint);
    }

    [Fact]
    public async Task DaysAreReadInOrderAndAnApiVersionCanBeNamed()
    {
        var (tools, fake, _) = Create();
        Configure(
            new
            {
                customerId = "1234567890",
                clientId = "fictional.apps.example.invalid",
                apiVersion = "v99",
            }
        );

        var report = await tools.InvokeAsync(AdsTools.Performance, new(Start, End, "date", 31), Token);

        Assert.Equal(["2026-03-01", "2026-03-02"], report.Rows.Select(r => r.Key));
        Assert.Equal(332.5m, report.Rows.Sum(r => r.Spend));
        Assert.All(fake.Requests.Skip(1), r => Assert.Contains("/v99/customers/1234567890/", r.Url));
        Assert.DoesNotContain(fake.Requests, r => r.Headers.ContainsKey("login-customer-id"));
    }

    [Fact]
    public async Task BadRequestsAreRefusedAndApiErrorsReportedOnce()
    {
        var (tools, fake, _) = Create();
        Configure(new { customerId = "1234567890", clientId = "fictional.apps.example.invalid" });
        Assert.Equal(
            422,
            (
                await Assert.ThrowsAsync<ToolFailureException>(() =>
                    tools.InvokeAsync(AdsTools.Performance, new(Start, End, "keyword", 10), Token)
                )
            ).Status
        );
        Assert.Empty(fake.Requests);

        fake.SearchStatus = HttpStatusCode.Forbidden;
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AdsTools.Performance, new(Start, End, "campaign", 10), Token)
        );
        Assert.Contains("HTTP 403, PERMISSION_DENIED", failure.Message);
        Assert.Single(fake.Requests, r => r.Url.Contains("googleAds:search", StringComparison.Ordinal));
        Assert.DoesNotContain("fictional-client-secret", failure.Message);
    }

    // Changes need the separate editing user's token and are registered only then (docs/adr/0033-approved-ad-changes.md).
    [Fact]
    public async Task ChangesUseTheirOwnSignInAndSendOneExactMutate()
    {
        Configure(new { customerId = "1234567890", clientId = "fictional.apps.example.invalid" });
        var readOnly = Create();
        Assert.Contains("read only, changes are off", readOnly.Provider.Status().Reason);
        Assert.False(readOnly.Provider.ChangesEnabled);

        var secrets = new Dictionary<string, string>(Secrets)
        {
            [GoogleAdsProvider.ChangeRefreshTokenSecret] = "fictional-change-token",
        };
        var fake = new Fake();
        var provider = new GoogleAdsProvider(_root, name => secrets.GetValueOrDefault(name), fake);
        var registry = new ToolRegistry(new MemoryToolAudit(), [new AllowAll()]);
        provider.Register(registry);
        var tools = registry.For(Manifest);

        var campaign = await tools.InvokeAsync(AdsTools.Campaign, new("111"), Token);
        Assert.Equal(new AdsCampaign("111", "Fictional search", "enabled", "7701", 50m, false, "EUR"), campaign);
        Assert.Contains("WHERE campaign.id = 111", fake.Requests[^1].Body);

        var authority = new ToolAuthority("fictional", "approval-1");
        await tools.InvokeAsync(
            AdsTools.Change,
            new("111", AdsTools.Budget, "enabled", "7701", 50m, 62.505m),
            Token,
            authority
        );
        await tools.InvokeAsync(
            AdsTools.Change,
            new("111", AdsTools.Pause, "enabled", null, null, null),
            Token,
            authority
        );

        var tokens = fake.Requests.Where(r => r.Url == GoogleServiceAccount.TokenEndpoint).ToArray();
        Assert.Contains(tokens, t => t.Body.Contains("refresh_token=fictional-refresh-token"));
        Assert.Contains(tokens, t => t.Body.Contains("refresh_token=fictional-change-token"));
        var mutates = fake.Requests.Where(r => r.Url.EndsWith(":mutate", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, mutates.Length);
        Assert.EndsWith("/customers/1234567890/campaignBudgets:mutate", mutates[0].Url);
        Assert.Contains("\"resourceName\":\"customers/1234567890/campaignBudgets/7701\"", mutates[0].Body);
        Assert.Contains("\"amountMicros\":\"62510000\"", mutates[0].Body);
        Assert.Contains("\"updateMask\":\"amount_micros\"", mutates[0].Body);
        Assert.EndsWith("/customers/1234567890/campaigns:mutate", mutates[1].Url);
        Assert.Contains("\"status\":\"PAUSED\"", mutates[1].Body);
        Assert.All(mutates, m => Assert.Equal("fictional-developer-token", m.Headers["developer-token"]));

        // A change that breaks the contract's rules is refused before anything is sent.
        var sent = fake.Requests.Count;
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AdsTools.Change, new("111", AdsTools.Pause, "paused", null, null, null), Token, authority)
        );
        Assert.Equal(sent, fake.Requests.Count);
    }

    private sealed class AllowAll : IToolAuthorityVerifier
    {
        public string Kind => "fictional";

        public bool Authorizes(ToolInvocation invocation) => true;
    }
}
