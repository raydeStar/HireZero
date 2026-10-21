using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Google;
using HireZero.Integrations.Synthetic;
using HireZero.Roles.Marketing;
using HireZero.Tools.Analytics;

// Marketing's analytics evidence (docs/adr/0029-marketing-analytics-reads.md) over fictional data: periods and changes
// computed deterministically, unusual days judged against the same kind of day, only Read tools called, and a clear
// refusal when no source is connected.
public sealed class MarketingAnalyticsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("marketing-analytics-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Audit : IToolAudit
    {
        public readonly List<ToolCallRecord> Calls = [];

        public void Record(ToolCallRecord record) => Calls.Add(record);
    }

    private static readonly EmployeeManifest Manifest = new MarketingModule().Manifest;

    private static (IToolbox Tools, Audit Audit) Toolbox(IToolProvider? provider)
    {
        var audit = new Audit();
        var registry = new ToolRegistry(audit);
        provider?.Register(registry);
        return (registry.For(Manifest), audit);
    }

    private static Dictionary<string, string> Days(string days) => new() { ["days"] = days };

    private static (string, double)[] Series(DateOnly start, params double[] values) =>
        [.. values.Select((v, i) => (start.AddDays(i).ToString("yyyy-MM-dd"), v))];

    [Fact]
    public void UnusualDaysAreJudgedAgainstTheSameKindOfDay()
    {
        // Monday 2026-01-05 to Sunday 2026-01-18: weekdays near 400, weekends near 250.
        var baseline = Series(
            new DateOnly(2026, 1, 5),
            400,
            410,
            395,
            405,
            398,
            250,
            260,
            402,
            399,
            407,
            393,
            401,
            255,
            245
        );
        var current = Series(new DateOnly(2026, 1, 19), 404, 1050, 396, 120, 400, 252, 248);

        var unusual = MarketingAnalytics.UnusualDays(baseline, current);

        // The spike and the drop are flagged; ordinary weekends are not.
        Assert.Equal(["2026-01-20", "2026-01-22"], unusual.Select(u => u.Day));
        Assert.True(unusual[0].Value > unusual[0].Expected && unusual[1].Value < unusual[1].Expected);
        Assert.Empty(MarketingAnalytics.UnusualDays(baseline[..3], current));
    }

    [Fact]
    public async Task TheReadComparesPeriodsAndFlagsThePlantedSpike()
    {
        var (tools, audit) = Toolbox(new SyntheticAnalyticsProvider());

        var text = await MarketingAnalytics.ReadAsync(Days("7"), tools, new DateOnly(2026, 2, 10), Token);

        Assert.Contains(
            "Traffic (Synthetic fictional data): 2026-02-03 to 2026-02-09, against 2026-01-27 to 2026-02-02.",
            text
        );
        Assert.Contains(
            "Search (Synthetic fictional data): 2026-02-01 to 2026-02-07, against 2026-01-25 to 2026-01-31",
            text
        );
        Assert.Contains("Unusual day in sessions: 2026-02-06", text);
        Assert.Contains("Unusual day in search clicks: 2026-02-06", text);
        Assert.Contains("Sessions by channel:", text);
        Assert.Contains("\"fictional widget\"", text);
        Assert.Contains("untrusted third-party data", text);
        Assert.True(text.Length <= 12000);

        // Deterministic: the same day reads the same evidence. Only Read tools were called.
        Assert.Equal(
            text,
            await MarketingAnalytics.ReadAsync(
                Days("7"),
                Toolbox(new SyntheticAnalyticsProvider()).Tools,
                new DateOnly(2026, 2, 10),
                Token
            )
        );
        Assert.All(audit.Calls, c => Assert.Equal(("read", "completed"), (c.Risk, c.Outcome)));
    }

    [Fact]
    public async Task ARefusalNamesWhatIsMissing()
    {
        var (synthetic, _) = Toolbox(new SyntheticAnalyticsProvider());
        Assert.Equal(
            422,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() =>
                    MarketingAnalytics.ReadAsync(Days("30"), synthetic, new DateOnly(2026, 2, 10), Token)
                )
            ).Status
        );
        var (none, _) = Toolbox(null);
        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            MarketingAnalytics.ReadAsync(Days("7"), none, new DateOnly(2026, 2, 10), Token)
        );
        Assert.Equal(409, error.Status);
        Assert.Contains("No analytics source is connected", error.Message);
    }

    [Fact]
    public void AFieldWithChoicesRefusesAnyOtherValue()
    {
        Assert.Equal(["7", "28", "90"], MarketingAnalytics.Definition.Fields[0].Choices!);
        Assert.Contains("28", CompanyStore.ActionInput(MarketingAnalytics.Definition, "{\"days\":\"28\"}"));
        var error = Assert.Throws<WorkspaceException>(() =>
            CompanyStore.ActionInput(MarketingAnalytics.Definition, "{\"days\":\"30\"}")
        );
        Assert.Equal((422, "Days: choose 7, 28, 90."), (error.Status, error.Message));
    }

    [Fact]
    public async Task AGoogleSourceWithOnlyAPropertyReadsTrafficAndSaysSearchWasNotRead()
    {
        File.WriteAllText(Path.Combine(_root, "google.json"), JsonSerializer.Serialize(new { ga4Property = "123456" }));
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var key = JsonSerializer.Serialize(
            new
            {
                type = "service_account",
                client_email = "reader@fictional-project.example.invalid",
                private_key = rsa.ExportPkcs8PrivateKeyPem(),
            }
        );
        var google = new GoogleAnalyticsProvider(_root, () => key, new GoogleFake());
        var (tools, _) = Toolbox(google);

        var text = await MarketingAnalytics.ReadAsync(Days("7"), tools, new DateOnly(2026, 2, 10), Token);

        Assert.Contains("Traffic (GA4 property 123456)", text);
        Assert.Contains(
            "Search: not read (Add a Search Console site in Company settings → Integrations → Google Analytics and Search Console.)",
            text
        );
    }

    // A newly verified Search Console property has no rows: the read says there is no data yet rather than printing
    // zeros and a 0.0 position as if they were measurements.
    [Fact]
    public async Task AnEmptySearchConsoleSaysThereIsNoDataYet()
    {
        File.WriteAllText(
            Path.Combine(_root, "google.json"),
            JsonSerializer.Serialize(new { searchConsoleSite = "sc-domain:fictional.example.invalid" })
        );
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var key = JsonSerializer.Serialize(
            new
            {
                type = "service_account",
                client_email = "reader@fictional-project.example.invalid",
                private_key = rsa.ExportPkcs8PrivateKeyPem(),
            }
        );
        var (tools, _) = Toolbox(new GoogleAnalyticsProvider(_root, () => key, new GoogleFake()));

        var text = await MarketingAnalytics.ReadAsync(Days("7"), tools, new DateOnly(2026, 2, 10), Token);

        Assert.Contains("No search data yet", text);
        Assert.DoesNotContain("Average position", text);
    }

    // Answers the token endpoint, every GA4 report with the same fictional day, and Search Console with no rows.
    private sealed class GoogleFake : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var json =
                request.RequestUri!.ToString() == GoogleServiceAccount.TokenEndpoint
                    ? """{"access_token":"fictional","expires_in":3600}"""
                : request.RequestUri.Host == "searchconsole.googleapis.com"
                    ? """{"responseAggregationType":"byProperty"}"""
                : """
                    {"rows":[{"dimensionValues":[{"value":"20260203"}],"metricValues":[{"value":"100"},{"value":"80"},{"value":"40"},{"value":"250"},{"value":"3"},{"value":"0.6"}]}],
                     "totals":[{"metricValues":[{"value":"100"},{"value":"80"},{"value":"40"},{"value":"250"},{"value":"3"},{"value":"0.6"}]}]}
                    """;
            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
