using System.Net;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.SiteAnalytics;
using HireZero.Tools.Analytics;

// A site's own counts (docs/adr/0173-site-analytics.md) against a fake handler with fictional MCP replies: two read-only
// tools/call requests to the named endpoint only, with the key, joined into one row per day. No call leaves the test.
public sealed class SiteAnalyticsProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "site-analytics-" + Guid.NewGuid().ToString("N"));

    public SiteAnalyticsProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string Endpoint = "https://www.example.com/mcp";
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 15, 0, 0, TimeSpan.Zero);

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Url, string? Auth, string Tool, int Days)> Requests = [];
        public HttpStatusCode Status = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            // The site's server reads exactly Content-Length bytes; a streamed body would arrive empty.
            Assert.NotNull(request.Content!.Headers.ContentLength);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
            var call = body.RootElement.GetProperty("params");
            var (tool, days) = (
                call.GetProperty("name").GetString()!,
                call.GetProperty("arguments").GetProperty("days").GetInt32()
            );
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), tool, days));
            // The site's last `days` UTC days, ending today, in its documented shapes.
            var dates = Enumerable
                .Range(0, days)
                .Select(i => new DateOnly(2026, 10, 31).AddDays(i - days + 1))
                .ToArray();
            object content =
                tool == "analytics_summary"
                    ? new
                    {
                        days,
                        views = 0,
                        series = dates.Select(d => new
                        {
                            day = d.ToString("yyyy-MM-dd"),
                            views = d.Day,
                            visitors = d.Day % 3,
                        }),
                    }
                    : new
                    {
                        days = dates.Select(d => new
                        {
                            date = d.ToString("yyyy-MM-dd"),
                            signups = d.Day == 20 ? 1 : 0,
                            accounts = 0,
                        }),
                        totals = new { accounts = 0 },
                    };
            var reply = JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = body.RootElement.GetProperty("id").GetInt32(),
                    result = new { content = Array.Empty<object>(), structuredContent = content },
                }
            );
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(reply, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private static readonly EmployeeManifest Manifest = new(
        "site-analytics-test",
        "Site analytics test",
        "Fictional manifest declaring the daily counts.",
        "1.0.0",
        "Fictional instructions.",
        [new("chat", "Conversation", "Fictional.")],
        new("Fictional", "Analytics tester", "Fictional.", [new("purpose", "Fictional purpose.")], "Fictional.")
    )
    {
        Tools = [new(AnalyticsTools.Daily.Id, ToolRisk.Read, "Daily counts.")],
    };

    private (IToolbox Tools, Fake Fake, SiteAnalyticsProvider Provider) Create(string? key, string? endpoint = Endpoint)
    {
        if (endpoint is not null)
            File.WriteAllText(
                Path.Combine(_root, "site-analytics.json"),
                JsonSerializer.Serialize(new { mcpUrl = endpoint })
            );
        var fake = new Fake();
        var provider = new SiteAnalyticsProvider(_root, () => key, fake, () => Now);
        var registry = new ToolRegistry(new MemoryToolAudit());
        provider.Register(registry);
        return (registry.For(Manifest), fake, provider);
    }

    [Fact]
    public async Task ItIsOffUntilAnAddressAndAKeyAreSetAndAcceptsOnlyASafeAddress()
    {
        var (tools, fake, provider) = Create(null, null);
        Assert.False(provider.Status().Active);
        Assert.Contains("MCP address", provider.Status().Reason);
        (tools, fake, provider) = Create(null);
        Assert.Contains("agent key", provider.Status().Reason);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AnalyticsTools.Daily, new(new(2026, 10, 19), new(2026, 10, 25)), Token)
        );
        Assert.Empty(fake.Requests);
        var check = provider.Setup.Fields.Single().Check!;
        Assert.Null(check(Endpoint));
        Assert.Null(check("http://127.0.0.1:8788/mcp"));
        foreach (
            var unsafeAddress in new[]
            {
                "http://www.example.com/mcp",
                "https://user:pw@www.example.com/mcp",
                "https://www.example.com/mcp?key=1",
                "mcp",
            }
        )
            Assert.NotNull(check(unsafeAddress));
        Assert.Equal([SiteAnalyticsProvider.KeySecret], provider.Setup.Secrets.Select(s => s.Name));
        (_, _, provider) = Create("hz_agent_fictional");
        Assert.True(provider.Status().Active);
        Assert.Contains("www.example.com", provider.Status().Reason);
    }

    [Fact]
    public async Task AWeekIsReadAsOneRowPerDayFromTwoReadOnlyCallsToTheSiteOnly()
    {
        var (tools, fake, _) = Create("hz_agent_fictional");
        var report = await tools.InvokeAsync(AnalyticsTools.Daily, new(new(2026, 10, 19), new(2026, 10, 25)), Token);

        // Days are counted back from today (October 31), so the asked-for week is inside both lists.
        Assert.Equal([("analytics_summary", 13), ("signups_daily", 13)], fake.Requests.Select(r => (r.Tool, r.Days)));
        Assert.All(fake.Requests, r => Assert.Equal((Endpoint, "Bearer hz_agent_fictional"), (r.Url, r.Auth)));
        Assert.Equal("Site analytics (www.example.com)", report.Source);
        Assert.Equal(7, report.Rows.Length);
        Assert.Equal(new DailyRow(new(2026, 10, 19), 19 % 3, 19, 0, 0), report.Rows[0]);
        Assert.Equal(1, report.Rows[1].SignUps);
        Assert.Equal(new DateOnly(2026, 10, 25), report.Rows[^1].Day);
    }

    [Fact]
    public async Task OlderDaysThanTheSiteKeepsAreUnknownAndARefusedKeyIsSaidPlainly()
    {
        var (tools, fake, _) = Create("hz_agent_fictional");
        var old = await tools.InvokeAsync(AnalyticsTools.Daily, new(new(2026, 7, 1), new(2026, 8, 3)), Token);
        Assert.Equal(
            [("analytics_summary", SiteAnalyticsProvider.ViewDays), ("signups_daily", 123)],
            fake.Requests.Select(r => (r.Tool, r.Days))
        );
        Assert.Null(old.Rows[0].Visitors);
        Assert.Equal(0, old.Rows[0].SignUps);
        Assert.NotNull(old.Rows[^1].Visitors);

        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AnalyticsTools.Daily, new(new(2026, 10, 30), new(2026, 11, 1)), Token)
        );
        fake.Status = HttpStatusCode.Unauthorized;
        var refused = await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AnalyticsTools.Daily, new(new(2026, 10, 19), new(2026, 10, 25)), Token)
        );
        Assert.Contains("refused the agent key", refused.Message);
    }
}
