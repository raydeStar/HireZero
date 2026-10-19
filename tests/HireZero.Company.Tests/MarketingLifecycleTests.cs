using HireZero.Core;
using HireZero.Integrations.Synthetic;
using HireZero.Roles.Marketing;
using HireZero.Tools.Crm;
using HireZero.Tools.Email;

// Email and CRM reads (docs/adr/0035-email-and-crm-read-contracts.md) over fictional aggregates: the contracts cannot
// carry personal data, the fixed rules flag list and pipeline problems, and the reads are deterministic and read-only.
public sealed class MarketingLifecycleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Audit : IToolAudit
    {
        public readonly List<ToolCallRecord> Calls = [];

        public void Record(ToolCallRecord record) => Calls.Add(record);
    }

    private static (IToolbox Tools, Audit Audit) Toolbox(params IToolProvider[] providers)
    {
        var audit = new Audit();
        var registry = new ToolRegistry(audit);
        foreach (var provider in providers)
            provider.Register(registry);
        return (registry.For(new MarketingModule().Manifest), audit);
    }

    private static Dictionary<string, string> Days(string days) => new() { ["days"] = days };

    // Aggregates only: the only text a record can carry is an id, a name, a source or a currency.
    [Fact]
    public void TheContractsCannotCarryPersonalData()
    {
        string[] allowed = ["Id", "Name", "Source", "Currency"];
        var records = typeof(EmailTools)
            .Assembly.GetTypes()
            .Concat(typeof(CrmTools).Assembly.GetTypes())
            .Where(t => t.IsPublic && t.GetMethod("<Clone>$") is not null);
        foreach (var record in records)
        foreach (var property in record.GetProperties().Where(p => p.PropertyType == typeof(string)))
            Assert.True(
                allowed.Contains(property.Name),
                record.Name + "." + property.Name + " could carry personal data."
            );
    }

    private static EmailCampaignRow Row(
        string name,
        long recipients,
        long bounces,
        long complaints,
        long unsubscribes
    ) =>
        new(
            name,
            name,
            new DateOnly(2026, 3, 3),
            recipients,
            recipients - bounces,
            recipients / 3,
            recipients / 30,
            unsubscribes,
            bounces,
            complaints
        );

    [Fact]
    public void EmailRulesFlagBouncesComplaintsAndUnsubscribes()
    {
        var report = new EmailReport(
            "fictional",
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 28),
            Row("total", 0, 0, 0, 0),
            [Row("clean", 10_000, 50, 2, 20), Row("bouncy", 10_000, 300, 0, 0), Row("noisy", 10_000, 0, 15, 80)]
        );

        var notes = EmailAttention(report);

        Assert.Contains(notes, n => n.StartsWith("bouncy: 3.0% bounced", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.StartsWith("noisy: spam complaints at 0.15%", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.StartsWith("noisy: 0.80% unsubscribed", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, n => n.StartsWith("clean", StringComparison.Ordinal));
    }

    private static string[] EmailAttention(EmailReport report) => MarketingLifecycle.EmailAttention(report);

    private static PipelineReport Pipeline(long won, long lost, decimal value, params SourceRow[] sources) =>
        new(
            "fictional",
            "XTS",
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 28),
            [],
            sources.Sum(s => s.NewContacts),
            sources.Sum(s => s.NewDeals),
            won,
            value,
            lost,
            0,
            null,
            sources
        );

    [Fact]
    public void PipelineRulesFlagASilentSourceAndFallingWins()
    {
        var before = Pipeline(20, 20, 50_000m, new SourceRow("Search", 100, 20, 10, 25_000m));
        var now = Pipeline(
            10,
            30,
            30_000m,
            new SourceRow("Search", 100, 20, 5, 15_000m),
            new SourceRow("Events", 40, 0, 0, 0)
        );

        var notes = MarketingLifecycle.PipelineAttention(now, before);

        Assert.Contains("Events: 40 new contacts (29% of all) and no deals.", notes);
        Assert.Contains("Win rate fell to 25.0% from 50.0%.", notes);
        Assert.Contains(
            notes,
            n => n.StartsWith("Won value fell to XTS 30,000.00 from XTS 50,000.00", StringComparison.Ordinal)
        );
        Assert.Empty(MarketingLifecycle.PipelineAttention(before, before));
    }

    [Fact]
    public async Task TheEmailReadFindsThePlantedProblems()
    {
        var (tools, audit) = Toolbox(new SyntheticEmailProvider());

        var text = await MarketingLifecycle.ReadEmailAsync(Days("90"), tools, new DateOnly(2026, 4, 1), Token);

        Assert.Contains("Account (Synthetic fictional email): 2026-01-01 to 2026-03-31", text);
        Assert.Contains("- Spring offer to imported list: 4.5% bounced", text);
        Assert.Contains("spam complaints at", text);
        Assert.Contains("Open rate (soft: privacy features inflate opens)", text);
        Assert.Equal(
            text,
            await MarketingLifecycle.ReadEmailAsync(
                Days("90"),
                Toolbox(new SyntheticEmailProvider()).Tools,
                new DateOnly(2026, 4, 1),
                Token
            )
        );
        Assert.All(audit.Calls, c => Assert.Equal(("read", "completed"), (c.Risk, c.Outcome)));
        Assert.All(
            SyntheticEmailProvider.Campaigns(new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 100)).Rows,
            r => Assert.Equal(DayOfWeek.Tuesday, r.Sent!.Value.DayOfWeek)
        );
    }

    [Fact]
    public async Task ThePipelineReadShowsStagesSourcesAndTheSilentSource()
    {
        var (tools, _) = Toolbox(new SyntheticCrmProvider());

        var text = await MarketingLifecycle.ReadPipelineAsync(Days("28"), tools, new DateOnly(2026, 4, 1), Token);

        Assert.Contains("CRM (Synthetic fictional CRM, XTS): 2026-03-04 to 2026-03-31", text);
        Assert.Contains("Open deals by stage now:\n- Lead:", text.ReplaceLineEndings("\n"));
        Assert.Contains("- Events:", text.Split("Needing attention")[1]);
        Assert.Contains("Editing the CRM is not possible", text);
    }

    [Fact]
    public async Task WithoutASourceTheReadsSaySoAndCheckTheirDays()
    {
        var (none, _) = Toolbox();
        Assert.Contains(
            "No email account is connected",
            (
                await Assert.ThrowsAsync<WorkspaceException>(() =>
                    MarketingLifecycle.ReadEmailAsync(Days("28"), none, new DateOnly(2026, 4, 1), Token)
                )
            ).Message
        );
        Assert.Contains(
            "No CRM is connected",
            (
                await Assert.ThrowsAsync<WorkspaceException>(() =>
                    MarketingLifecycle.ReadPipelineAsync(Days("28"), none, new DateOnly(2026, 4, 1), Token)
                )
            ).Message
        );
        Assert.Equal(
            422,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() =>
                    MarketingLifecycle.ReadEmailAsync(
                        Days("7"),
                        Toolbox(new SyntheticEmailProvider()).Tools,
                        new DateOnly(2026, 4, 1),
                        Token
                    )
                )
            ).Status
        );
        Assert.Throws<ToolFailureException>(() =>
            SyntheticEmailProvider.Campaigns(new(new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1), 10))
        );
        Assert.Throws<ToolFailureException>(() =>
            SyntheticCrmProvider.Pipeline(new(new DateOnly(2026, 1, 1), new DateOnly(2027, 6, 1)))
        );
    }
}
