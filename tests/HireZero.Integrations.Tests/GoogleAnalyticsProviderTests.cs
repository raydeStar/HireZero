using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Google;
using HireZero.Tools.Analytics;

// Google analytics reads (docs/adr/0029-marketing-analytics-reads.md) against a fake handler with fictional API
// responses and a key generated per test: a signed, read-only sign-in to Google's token endpoint only, GA4 and Search
// Console requests in their documented shapes, and refusals before anything is sent. No call reaches Google.
public sealed class GoogleAnalyticsProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "google-" + Guid.NewGuid().ToString("N"));
    private readonly RSA _rsa = RSA.Create(2048);

    public GoogleAnalyticsProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _rsa.Dispose();
        Directory.Delete(_root, true);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Key(string? tokenUri = GoogleServiceAccount.TokenEndpoint) =>
        JsonSerializer.Serialize(
            new Dictionary<string, string?>
            {
                ["type"] = "service_account",
                ["client_email"] = "reader@fictional-project.example.invalid",
                ["private_key_id"] = "fictional-key-id",
                ["private_key"] = _rsa.ExportPkcs8PrivateKeyPem(),
                ["token_uri"] = tokenUri,
            }
        );

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Url, string? Auth, string Body)> Requests = [];
        public HttpStatusCode DataStatus = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            var url = request.RequestUri!.ToString();
            var body = await request.Content!.ReadAsStringAsync(token);
            Requests.Add((url, request.Headers.Authorization?.ToString(), body));
            string json;
            var status = HttpStatusCode.OK;
            if (url == GoogleServiceAccount.TokenEndpoint)
                json = """{"access_token":"fictional-access-token","expires_in":3600,"token_type":"Bearer"}""";
            else if (url.StartsWith("https://analyticsdata.googleapis.com/", StringComparison.Ordinal))
            {
                status = DataStatus;
                json =
                    status == HttpStatusCode.OK
                        ? """
                            {"rows":[
                              {"dimensionValues":[{"value":"20261001"}],"metricValues":[{"value":"120"},{"value":"100"},{"value":"60"},{"value":"300"},{"value":"4"},{"value":"0.61"}]},
                              {"dimensionValues":[{"value":"20261002"}],"metricValues":[{"value":"80"},{"value":"70"},{"value":"40"},{"value":"200"},{"value":"2"},{"value":"0.55"}]}],
                             "totals":[{"dimensionValues":[{"value":"RESERVED_TOTAL"}],"metricValues":[{"value":"200"},{"value":"160"},{"value":"100"},{"value":"500"},{"value":"6"},{"value":"0.586"}]}]}
                            """
                        : """{"error":{"code":403,"message":"fictional denial","status":"PERMISSION_DENIED"}}""";
            }
            else if (body.Contains("\"dimensions\"", StringComparison.Ordinal))
                json = """
                    {"rows":[{"keys":["fictional widget"],"clicks":40,"impressions":900,"ctr":0.0444,"position":5.2},
                             {"keys":["example widget pricing"],"clicks":12,"impressions":400,"ctr":0.03,"position":8.1}]}
                    """;
            else
                json = """{"rows":[{"clicks":52,"impressions":1300,"ctr":0.04,"position":6.1}]}""";
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly EmployeeManifest Manifest = new(
        "analytics-test",
        "Analytics test",
        "Fictional manifest declaring the analytics tools.",
        "1.0.0",
        "Fictional instructions.",
        [new("chat", "Conversation", "Fictional.")],
        new("Fictional", "Analytics tester", "Fictional.", [new("purpose", "Fictional purpose.")], "Fictional.")
    )
    {
        Tools =
        [
            new(AnalyticsTools.Status.Id, ToolRisk.Read, "Status."),
            new(AnalyticsTools.Traffic.Id, ToolRisk.Read, "Traffic."),
            new(AnalyticsTools.Search.Id, ToolRisk.Read, "Search."),
        ],
    };

    private (IToolbox Tools, Fake Fake, GoogleAnalyticsProvider Provider) Create(string? key)
    {
        var fake = new Fake();
        var provider = new GoogleAnalyticsProvider(_root, () => key, fake);
        var registry = new ToolRegistry(new MemoryToolAudit());
        provider.Register(registry);
        return (registry.For(Manifest), fake, provider);
    }

    private void Configure(string? property, string? site) =>
        File.WriteAllText(
            Path.Combine(_root, "google.json"),
            JsonSerializer.Serialize(new { ga4Property = property, searchConsoleSite = site })
        );

    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2026, 10, 2);

    [Fact]
    public async Task NothingIsSentUntilAKeyAndASourceAreSet()
    {
        var (tools, fake, provider) = Create(null);
        Assert.Contains(GoogleAnalyticsProvider.KeySecret, provider.Status().Reason);
        (tools, fake, provider) = Create(Key());
        Assert.Contains("google.json", provider.Status().Reason);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AnalyticsTools.Traffic, new(Start, End, null, 10), Token)
        );
        Assert.Contains(
            "token endpoint other than Google's",
            Create(Key("https://token.example.invalid/")).Provider.Status().Reason
        );
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task TrafficIsReadFromGa4WithASignedReadOnlySignIn()
    {
        var (tools, fake, provider) = Create(Key());
        Configure("properties/123456", null);
        Assert.True(provider.Status().Active);

        var report = await tools.InvokeAsync(AnalyticsTools.Traffic, new(Start, End, "date", 10), Token);

        Assert.Equal("GA4 property 123456", report.Source);
        Assert.Equal(new TrafficRow("total", 200, 160, 100, 500, 6, 0.586), report.Totals);
        Assert.Equal(["2026-10-01", "2026-10-02"], report.Rows.Select(r => r.Key));
        var (signIn, data) = (fake.Requests[0], fake.Requests[1]);
        Assert.Equal(GoogleServiceAccount.TokenEndpoint, signIn.Url);
        var assertion = signIn.Body.Split("assertion=")[1];
        var parts = Uri.UnescapeDataString(assertion).Split('.');
        static byte[] Decode(string part) =>
            Convert.FromBase64String(part.Replace('-', '+').Replace('_', '/').PadRight((part.Length + 3) / 4 * 4, '='));
        Assert.True(
            _rsa.VerifyData(
                Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
                Decode(parts[2]),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            )
        );
        using var claims = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal(GoogleServiceAccount.Scopes, claims.RootElement.GetProperty("scope").GetString());
        Assert.Equal(GoogleServiceAccount.TokenEndpoint, claims.RootElement.GetProperty("aud").GetString());

        Assert.Equal("https://analyticsdata.googleapis.com/v1beta/properties/123456:runReport", data.Url);
        Assert.Equal("Bearer fictional-access-token", data.Auth);
        using var body = JsonDocument.Parse(data.Body);
        Assert.Equal("date", body.RootElement.GetProperty("dimensions")[0].GetProperty("name").GetString());
        Assert.Equal("2026-10-01", body.RootElement.GetProperty("dateRanges")[0].GetProperty("startDate").GetString());

        // The token is reused until it nears expiry.
        await tools.InvokeAsync(AnalyticsTools.Traffic, new(Start, End, null, 10), Token);
        Assert.Single(fake.Requests, r => r.Url == GoogleServiceAccount.TokenEndpoint);
    }

    [Fact]
    public async Task SearchIsReadFromSearchConsoleWithTotalsAndRows()
    {
        var (tools, fake, _) = Create(Key());
        Configure(null, "sc-domain:example.invalid");

        var report = await tools.InvokeAsync(AnalyticsTools.Search, new(Start, End, "query", 5), Token);

        Assert.Equal(new SearchRow("total", 52, 1300, 0.04, 6.1), report.Totals);
        Assert.Equal(["fictional widget", "example widget pricing"], report.Rows.Select(r => r.Key));
        Assert.All(
            fake.Requests.Skip(1),
            r =>
                Assert.Equal(
                    "https://searchconsole.googleapis.com/webmasters/v3/sites/sc-domain%3Aexample.invalid/searchAnalytics/query",
                    r.Url
                )
        );
        // Traffic is refused without a property, before anything more is sent.
        var sent = fake.Requests.Count;
        var refused = await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AnalyticsTools.Traffic, new(Start, End, null, 10), Token)
        );
        Assert.Equal(409, refused.Status);
        Assert.Equal(sent, fake.Requests.Count);
    }

    [Fact]
    public async Task BadRequestsAndGoogleErrorsAreReportedNotRetried()
    {
        var (tools, fake, _) = Create(Key());
        Configure("123456", "https://www.example.invalid/");
        foreach (
            var bad in new TrafficRequest[]
            {
                new(End, Start, null, 10),
                new(Start, Start.AddDays(AnalyticsTools.MaxDays), null, 10),
                new(Start, End, "campaign", 10),
                new(Start, End, null, 0),
            }
        )
            Assert.Equal(
                422,
                (
                    await Assert.ThrowsAsync<ToolFailureException>(() =>
                        tools.InvokeAsync(AnalyticsTools.Traffic, bad, Token)
                    )
                ).Status
            );
        Assert.Empty(fake.Requests);

        fake.DataStatus = HttpStatusCode.Forbidden;
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AnalyticsTools.Traffic, new(Start, End, null, 10), Token)
        );
        Assert.Contains("HTTP 403, PERMISSION_DENIED", failure.Message);
        Assert.Single(fake.Requests, r => r.Url.Contains("runReport", StringComparison.Ordinal));
    }
}
