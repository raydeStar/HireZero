using System.Text.Json;
using HireZero.Core;
using HireZero.Tools.Web;
using Microsoft.Data.Sqlite;

public sealed class BusinessFreshnessTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private const string Original = "We make paper craft kits.";
    private readonly TemporaryRoot _root = new("business-freshness-");
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-25T00:00:00Z");

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-runtime",
            companyId: company
        )
        {
            Clock = () => _now,
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    private static CompanyCommand Exact(CompanyBusinessProfile p, string action) =>
        C(action) with
        {
            Id = p.Id,
            ExpectedVersion = p.Version,
            Digest = p.Digest,
        };

    private static (CompanyBusinessProfile Profile, CompanyBusinessSource Source) Seed(CompanyStore s)
    {
        var identity = Apply<CompanyBusinessIdentity>(
            s,
            C("business.identify") with
            {
                Body = CompanyJson.Encode(
                    new CompanyBusinessInput(
                        "Fictional Paper Studio",
                        "https://paper.example.invalid/",
                        "Plan a launch"
                    )
                ),
            }
        );
        identity = Apply<CompanyBusinessIdentity>(
            s,
            C("business.confirm") with
            {
                ExpectedVersion = identity.Version,
                Digest = identity.Digest,
            }
        );
        var source = s.RecordBusinessSource(
            Owner,
            identity.Version,
            "https://paper.example.invalid/about",
            "About",
            Original,
            false
        );
        var fields = CompanyStore
            .BusinessProfileFields.Select(k =>
                k == "business"
                    ? new CompanyProfileField(k, "Paper craft kits", "sourced", [new(source.Id, Original)])
                    : new CompanyProfileField(k, "Unknown", "unknown", [])
            )
            .ToArray();
        var profile = Apply<CompanyBusinessProfile>(
            s,
            C("business.profile.save") with
            {
                Body = CompanyJson.Encode(new CompanyProfileInput(identity.Version, fields)),
            }
        );
        profile = Apply<CompanyBusinessProfile>(
            s,
            Exact(profile, "business.profile.review") with
            {
                Decision = "approve",
            }
        );
        return (profile, source);
    }

    private static CompanyCommand Start(CompanyBusinessProfile p, CompanyBusinessSource s) =>
        Exact(p, "business.refresh.start") with
        {
            Decision = "refresh-approved-sources",
            Evidence = [s.Id],
        };

    private static CompanyCommand Review(
        CompanyStore s,
        CompanyBusinessProfile p,
        string status,
        string? source = null,
        string? quote = null
    )
    {
        var field = s.BusinessFreshness(p.Id).Fields.Single(f => f.Field == "business");
        return Exact(p, "business.fact.review") with
        {
            Body = CompanyJson.Encode(
                new CompanyFactReviewInput(
                    field.Field,
                    field.EvidenceDigest,
                    field.Review?.Id,
                    status,
                    "Fictional owner checked the offering.",
                    source,
                    quote
                )
            ),
        };
    }

    private static CompanySourceRefresh Check(
        CompanyStore store,
        CompanyBusinessProfile p,
        CompanyBusinessSource source,
        string text
    )
    {
        var run = Apply<CompanySourceRefresh>(store, Start(p, source));
        var observed = store.RecordBusinessSource(Owner, p.BusinessVersion, source.Url, source.Title, text, false);
        return store.CompleteBusinessRefresh(run.Id, Owner, [new(source.Id, observed.Id)]);
    }

    private static CompanyAttempt Prepare(CompanyStore s, CompanyTask work)
    {
        using var result = JsonDocument.Parse(
            s.Apply(
                C("conversation.prepare") with
                {
                    EmployeeId = work.EmployeeId,
                    ThreadId = work.ThreadId,
                    Scope = "employee",
                    Body = "Outline the paper kit launch",
                    CreditAllowance = 1,
                }
            )
        );
        return result.RootElement.GetProperty("attempt").Deserialize<CompanyAttempt>(CompanyJson.Options)!;
    }

    private static CompanyCommand Run(CompanyAttempt a) =>
        C("measured.run") with
        {
            Id = a.Id,
            ExpectedVersion = a.Version,
            Digest = a.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    [Fact]
    public async Task RefreshIsSingleUseAndKeepsCheckDatesSeparateFromApprovedSnapshots()
    {
        var store = Store();
        var (profile, source) = Seed(store);
        var tools = new ToolRegistry(store);
        var calls = 0;
        var text = Original;
        tools.Register(
            WebTools.Research,
            (request, _) =>
            {
                calls++;
                Assert.Equal(source.Url, request.Url);
                return Task.FromResult(new WebPage(source.Url, source.Title, text, false));
            }
        );
        _now = _now.AddDays(2);
        var command = Start(profile, source);
        var receipt = await CompanyBusinessResearch.RunRefreshAsync(
            command,
            Owner,
            store,
            tools,
            TestContext.Current.CancellationToken
        );
        await CompanyBusinessResearch.RunRefreshAsync(
            command,
            Owner,
            store,
            tools,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(1, calls);
        var run = store.BusinessRefresh(CompanyJson.Decode<CompanySourceRefresh>(receipt).Id);
        var check = Assert.Single(run.Checks!);
        Assert.Equal("unchanged", check.Status);
        Assert.Equal(source.Id, check.ObservedSourceId);
        Assert.Equal(_now, check.CheckedAt);
        Assert.Equal(source.RetrievedAt, store.BusinessSource(source.Id).RetrievedAt);
        text = "We now offer only studio workshops.";
        await CompanyBusinessResearch.RunRefreshAsync(
            Start(profile, source),
            Owner,
            store,
            tools,
            TestContext.Current.CancellationToken
        );
        var state = store.BusinessFreshness(profile.Id);
        Assert.Equal("possible", state.Fields.Single(f => f.Field == "business").Status);
        Assert.False(state.Blocked);
        Assert.Equal(
            CompanyJson.Encode(profile),
            CompanyJson.Encode(MeasuredQueueChecks.Items<CompanyBusinessProfile>(store, "businessProfiles").Single())
        );
        Assert.Equal(CompanyJson.Encode(state), CompanyJson.Encode(Store().BusinessFreshness(profile.Id)));
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void OwnerReviewsBindExactEvidenceAndCriticalAssessmentsSurviveLaterChecks()
    {
        var s = Store();
        var (p, source) = Seed(s);
        var run = Check(s, p, source, Original + " New hours available.");
        var observed = run.Checks![0].ObservedSourceId!;
        Assert.Throws<WorkspaceException>(() => s.Apply(Review(s, p, "verified", observed, "Invented quote")));
        var verified = Review(s, p, "verified", observed, Original);
        s.Apply(verified);
        Assert.Equal(s.Apply(verified), s.Operation(verified.RequestId));
        Assert.Equal("verified", s.BusinessFreshness(p.Id).Fields[0].Status);
        var stale = Review(s, p, "possible");
        s.Apply(Review(s, p, "outdated"));
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => s.Apply(stale)).Status);
        Check(s, p, source, Original);
        Assert.True(s.BusinessFreshness(p.Id).Blocked);
        Assert.Equal("outdated", s.BusinessFreshness(p.Id).Fields[0].Status);
        // Resolving an assessment is another exact owner action, never a side effect of a network response.
        s.Apply(Review(s, p, "verified", source.Id, Original));
        Assert.False(s.BusinessFreshness(p.Id).Blocked);
        _now = _now.AddHours(2);
        Check(s, p, source, Original);
        Assert.Equal("verified", s.BusinessFreshness(p.Id).Fields[0].Status);
        Check(s, p, source, "The studio now offers workshops only.");
        Assert.Equal("possible", s.BusinessFreshness(p.Id).Fields[0].Status);
    }

    [Fact]
    public void FreshnessChangesFencePreparedAndLateWorkWithoutDiscardingReportedUsage()
    {
        var s = Store();
        var (p, source) = Seed(s);
        var work = MeasuredQueueChecks.Seed(s);
        var prepared = Prepare(s, work);
        Check(s, p, source, Original);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(prepared)));
        var other = Store("co-late-freshness");
        var (op, os) = Seed(other);
        var ow = MeasuredQueueChecks.Seed(other);
        Check(other, op, os, Original);
        var late = Prepare(other, ow);
        Assert.Equal("unchanged", other.TextCall(late.Id).Context.BusinessBrief!.Freshness!.Fields.Single().Status);
        other.Apply(Run(late));
        other.Apply(Review(other, op, "conflicting"));
        Assert.False(other.IsCurrent(late.Id));
        other.Finish(late.Id, new("Fictional late response", 30, "fictional-freshness-receipt", new(20, 10)));
        Assert.Equal("withheld", other.Attempt(late.Id).Phase);
        Assert.Equal(30, other.Attempt(late.Id).ReportedUsage);
        Assert.Empty(MeasuredQueueChecks.Items<CompanyArtifact>(other, "artifacts"));
        Assert.Contains("conflicting or outdated", Assert.Throws<WorkspaceException>(() => Prepare(other, ow)).Message);
        other.Apply(Exact(op, "business.profile.review") with { Decision = "revoke" });
        var replacement = Apply<CompanyBusinessProfile>(
            other,
            C("business.profile.save") with
            {
                Body = CompanyJson.Encode(new CompanyProfileInput(op.BusinessVersion, op.Fields)),
            }
        );
        replacement = Apply<CompanyBusinessProfile>(
            other,
            Exact(replacement, "business.profile.review") with
            {
                Decision = "approve",
            }
        );
        Assert.False(other.BusinessFreshness(replacement.Id).Blocked);
        Assert.NotNull(Prepare(other, ow));
    }

    [Fact]
    public async Task PartialAndInterruptedReadsNeverOverwriteFactsOrReplayOnRestart()
    {
        var s = Store();
        var (p, source) = Seed(s);
        var tools = new ToolRegistry(s);
        tools.Register<WebResearchRequest, WebPage>(
            WebTools.Research,
            (_, _) => throw new ToolFailureException("refused", "Robots policy disallows this source.", 422)
        );
        var receipt = await CompanyBusinessResearch.RunRefreshAsync(
            Start(p, source),
            Owner,
            s,
            tools,
            TestContext.Current.CancellationToken
        );
        var run = s.BusinessRefresh(CompanyJson.Decode<CompanySourceRefresh>(receipt).Id);
        Assert.Equal("partial", run.State);
        Assert.Equal("unavailable", Assert.Single(run.Checks!).Status);
        Assert.False(s.BusinessFreshness(p.Id).Blocked);
        var interrupted = Apply<CompanySourceRefresh>(s, Start(p, source));
        s.Recover();
        Assert.Equal("unknown", Store().BusinessRefresh(interrupted.Id).State);
        Assert.False(s.MayRefreshBusinessSource(interrupted.Id, source.Id));
        Assert.Equal("unknown", s.CompleteBusinessRefresh(interrupted.Id, Owner, [new(source.Id, source.Id)]).State);
        var late = Apply<CompanySourceRefresh>(s, Start(p, source));
        s.Apply(Exact(p, "business.profile.review") with { Decision = "revoke" });
        Assert.Equal("withheld", s.CompleteBusinessRefresh(late.Id, Owner, [new(source.Id, source.Id)]).State);
    }

    [Fact]
    public void RefreshAndReviewsEnforceOwnerTenantExactApprovalAndHourlyBounds()
    {
        var s = Store();
        var (p, source) = Seed(s);
        var other = Store("co-neighbor-freshness");
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.BusinessFreshness(p.Id)).Status);
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.Apply(Start(p, source))).Status);
        Assert.Throws<WorkspaceException>(() => s.Apply(Start(p, source) with { Digest = "wrong" }));
        Assert.Throws<WorkspaceException>(() => s.Apply(Start(p, source) with { Evidence = ["uncited"] }));
        s.Apply(C("member.invite") with { Body = "approver@example.invalid", Decision = "approver" });
        var member = s.SignIn("fictional-approver", "approver@example.invalid", true, "Fictional Approver")!;
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => s.Apply(Start(p, source), member.Subject, out _)).Status
        );
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => s.Apply(Review(s, p, "outdated"), member.Subject, out _)).Status
        );
        for (var i = 0; i < 3; i++)
            Check(s, p, source, Original);
        Assert.Equal(429, Assert.Throws<WorkspaceException>(() => s.Apply(Start(p, source))).Status);
        _now = _now.AddHours(2);
        var running = Apply<CompanySourceRefresh>(s, Start(p, source));
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.BusinessRefresh(running.Id)).Status);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => s.Apply(Start(p, source))).Status);
    }

    [Fact]
    public void FreshnessMigrationPreservesEarlierDataAndChecksums()
    {
        var s = Store();
        var (p, _) = Seed(s);
        string[] checksums;
        using (var db = new StoreConnection(s.Database.Open(), s.Database))
        {
            checksums = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE IF EXISTS co_document_retention; DROP TABLE IF EXISTS co_sandbox_jobs; DROP TABLE IF EXISTS co_compute_policy; DROP TABLE IF EXISTS co_workspace_leases; DROP TABLE IF EXISTS co_usage_entries; DROP TABLE IF EXISTS co_usage_policy; DROP TABLE IF EXISTS co_memory_terms; DROP TABLE IF EXISTS co_memory; DROP TABLE IF EXISTS co_notice_reads; DROP TABLE IF EXISTS co_notice_deliveries; DROP TABLE IF EXISTS co_notice_preferences; DROP TABLE IF EXISTS co_verified_emails; DROP TABLE IF EXISTS co_notices; DROP TABLE co_schedule_runs; DROP TABLE co_schedules; DROP TABLE co_business_fact_reviews; DROP TABLE co_business_checks; DROP TABLE co_business_refreshes; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=15;"
            );
        }
        var reopened = Store();
        Assert.False(reopened.BusinessFreshness(p.Id).Blocked);
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal(
            checksums,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            )
        );
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
