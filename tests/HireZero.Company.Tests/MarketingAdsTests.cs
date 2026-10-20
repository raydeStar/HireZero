using HireZero.Core;
using HireZero.Integrations.Synthetic;
using HireZero.Roles.Marketing;
using HireZero.Tools.Advertising;

// Marketing's ads evidence (docs/adr/0031-ads-read-contracts.md) over fictional data in XTS: request rules every
// provider applies, fixed attention rules, and a deterministic read that lists the planted situations and calls only
// Read tools.
public sealed class MarketingAdsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Audit : IToolAudit
    {
        public readonly List<ToolCallRecord> Calls = [];

        public void Record(ToolCallRecord record) => Calls.Add(record);
    }

    private static (IToolbox Tools, Audit Audit) Toolbox(IToolProvider? provider)
    {
        var audit = new Audit();
        var registry = new ToolRegistry(audit);
        provider?.Register(registry);
        return (registry.For(new MarketingModule().Manifest), audit);
    }

    private static Dictionary<string, string> Days(string days) => new() { ["days"] = days };

    [Fact]
    public async Task AnAdChangeOffersTheAccountsCampaignsByName()
    {
        Assert.True(MarketingAdChange.Definition.Fields.Single(f => f.Id == "campaign").LoadsChoices);
        var (tools, audit) = Toolbox(new SyntheticAdsProvider());
        var choices = await new MarketingModule().ActionChoicesAsync(
            MarketingAdChange.Action,
            "campaign",
            tools.ReadOnly(),
            Token
        );
        Assert.NotEmpty(choices);
        Assert.All(choices, c => Assert.Matches(@"\((enabled|paused|unknown)\)$", c.Label));
        // A listed campaign is one the ad change can read: the same id across ads.performance and ads.campaign.
        var enabled = choices.First(c => c.Label.EndsWith("(enabled)", StringComparison.Ordinal));
        var reading = await MarketingAdChange.ReadAsync(enabled.Value, AdsTools.Pause, "0", tools.ReadOnly(), Token);
        Assert.NotNull(reading.Step);
        Assert.All(audit.Calls, c => Assert.Equal("read", c.Risk));
        Assert.Empty(await MarketingAdChange.CampaignChoicesAsync(Toolbox(null).Tools, new(2026, 3, 1), Token));
        Assert.Equal(
            404,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() =>
                    new MarketingModule().ActionChoicesAsync(MarketingAds.ReadAction, "days", tools, Token)
                )
            ).Status
        );
    }

    [Fact]
    public void EveryProviderRefusesTheSameBadRequests()
    {
        var day = new DateOnly(2026, 3, 1);
        foreach (
            var bad in new AdsRequest[]
            {
                new(day, day.AddDays(-1), "campaign", 10),
                new(day, day.AddDays(AdsTools.MaxDays), "campaign", 10),
                new(day, day, "keyword", 10),
                new(day, day, "date", 101),
            }
        )
            Assert.Equal(422, Assert.Throws<ToolFailureException>(() => SyntheticAdsProvider.Performance(bad)).Status);
    }

    [Fact]
    public void SyntheticAdsAreDeterministicInTheTestCurrency()
    {
        var request = new AdsRequest(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 7), "campaign", 100);
        var report = SyntheticAdsProvider.Performance(request);
        Assert.Equal("XTS", report.Currency);
        Assert.Equal(report, SyntheticAdsProvider.Performance(request) with { Rows = report.Rows });
        Assert.Equal(report.Totals.Spend, report.Rows.Sum(r => r.Spend));
        Assert.Contains(report.Rows, r => r.Status == "paused" && r.Spend == 0);
        var daily = SyntheticAdsProvider.Performance(request with { Breakdown = "date" });
        Assert.Equal(7, daily.Rows.Length);
        Assert.Equal(report.Totals.Spend, daily.Rows.Sum(r => r.Spend));
    }

    private static AdsRow Row(string key, decimal spend, double conversions, decimal value) =>
        new(key, key, "enabled", 10_000, (long)(spend * 2), spend, conversions, value);

    private static AdsReport Report(params AdsRow[] rows) =>
        new(
            "fictional",
            "XTS",
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 7),
            "campaign",
            new(
                "total",
                null,
                null,
                0,
                0,
                rows.Sum(r => r.Spend),
                rows.Sum(r => r.Conversions),
                rows.Sum(r => r.ConversionValue)
            ),
            rows
        );

    [Fact]
    public void FixedRulesListOnlyMaterialProblems()
    {
        var previous = Report(
            Row("steady", 500, 50, 2000),
            Row("worse", 300, 30, 900),
            Row("silent", 100, 2, 50),
            Row("tiny", 10, 0, 0)
        );
        var current = Report(
            Row("steady", 500, 52, 2100),
            Row("worse", 300, 20, 250),
            Row("silent", 100, 0, 0),
            Row("tiny", 10, 0, 0)
        );

        var notes = MarketingAds.Attention(current, previous);

        Assert.Contains(
            notes,
            n => n.StartsWith("silent: spent XTS 100.00", StringComparison.Ordinal) && n.Contains("no conversions")
        );
        Assert.Contains(
            notes,
            n => n.StartsWith("worse: cost per conversion rose to XTS 15.00 from XTS 10.00", StringComparison.Ordinal)
        );
        Assert.Contains(notes, n => n.StartsWith("worse: return on ad spend 0.83x", StringComparison.Ordinal));
        Assert.DoesNotContain(
            notes,
            n => n.StartsWith("steady", StringComparison.Ordinal) || n.StartsWith("tiny", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task TheReadListsThePlantedSituations()
    {
        var (tools, audit) = Toolbox(new SyntheticAdsProvider());

        var text = await MarketingAds.ReadAsync(Days("28"), tools, new DateOnly(2026, 4, 1), Token);

        Assert.Contains(
            "Account (Synthetic fictional ads, XTS): 2026-03-04 to 2026-03-31, against 2026-02-04 to 2026-03-03.",
            text
        );
        Assert.Contains("- Video awareness: spent XTS", text);
        Assert.Contains("- Prospecting social: cost per conversion rose", text);
        Assert.Contains("- Prospecting social: return on ad spend", text);
        Assert.DoesNotContain("- Brand search:", text.Split("Campaigns by spend:")[0]);
        Assert.Contains("Unusual day in spend: 2026-03-23", text);
        Assert.Contains("Spring promotion (paused)", text);
        Assert.Contains("any change would need the owner's approval", text);
        Assert.Equal(
            text,
            await MarketingAds.ReadAsync(
                Days("28"),
                Toolbox(new SyntheticAdsProvider()).Tools,
                new DateOnly(2026, 4, 1),
                Token
            )
        );
        Assert.All(audit.Calls, c => Assert.Equal(("read", "completed"), (c.Risk, c.Outcome)));
    }

    [Fact]
    public async Task WithoutAnAdsAccountTheReadSaysSo()
    {
        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            MarketingAds.ReadAsync(Days("7"), Toolbox(null).Tools, new DateOnly(2026, 4, 1), Token)
        );
        Assert.Equal(409, error.Status);
        Assert.Contains("No ads account is connected", error.Message);
        Assert.Equal(
            422,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() =>
                    MarketingAds.ReadAsync(
                        Days("14"),
                        Toolbox(new SyntheticAdsProvider()).Tools,
                        new DateOnly(2026, 4, 1),
                        Token
                    )
                )
            ).Status
        );
    }
}
