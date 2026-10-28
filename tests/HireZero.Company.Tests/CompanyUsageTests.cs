using System.Text.Json;
using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyUsageTests : IDisposable
{
    private readonly TemporaryRoot _root = new("company-usage-");
    private DateTimeOffset _now = new(2026, 10, 31, 23, 58, 0, TimeSpan.Zero);

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId, ModelPrice? price = null) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-runtime",
            companyId: company
        )
        {
            Clock = () => _now,
            UsagePrice = _ => price,
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    private static CompanyUsagePolicy Configure(CompanyStore s, int jobs, long tokens, bool paused = false) =>
        Apply<CompanyUsagePolicy>(
            s,
            C("usage.configure") with
            {
                ExpectedVersion = s.Usage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyUsageLimits(jobs, tokens, paused)),
            }
        );

    private static CompanyAttempt Prepare(CompanyStore s, CompanyTask work)
    {
        using var json = JsonDocument.Parse(
            s.Apply(
                C("conversation.prepare") with
                {
                    EmployeeId = work.EmployeeId,
                    ThreadId = work.ThreadId,
                    Scope = "employee",
                    Body = "Draft an internal paper campaign note",
                    CreditAllowance = 1,
                }
            )
        );
        return json.RootElement.GetProperty("attempt").Deserialize<CompanyAttempt>(CompanyJson.Options)!;
    }

    private static CompanyCommand Run(CompanyAttempt a) =>
        C("measured.run") with
        {
            Id = a.Id,
            ExpectedVersion = a.Version,
            Digest = a.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    private static void Finish(CompanyStore s, CompanyAttempt a, decimal? cost = null) =>
        s.Finish(a.Id, new("Fictional internal reply", 30, "fictional-usage-receipt", new(20, 10, BilledUsd: cost)));

    [Fact]
    public void LimitsApplyAtDispatchAndSettledJobsCannotBeCountedTwice()
    {
        var s = Store();
        var task = MeasuredQueueChecks.Seed(s);
        Configure(s, 1, 14_000);
        var first = Prepare(s, task);
        Assert.Equal(0, s.Usage().Current.Jobs);
        s.Apply(Run(first));
        Assert.Equal(1, s.Usage().Current.Jobs);
        Assert.Equal(14_000, s.Usage().Current.HeldTokens);
        Finish(s, first);
        Finish(s, first);
        Assert.Equal(30, s.Usage().Current.Tokens);
        Assert.Equal(0, s.Usage().Current.HeldTokens);
        Assert.Single(s.Usage().Entries);
        var next = Prepare(s, task);
        Assert.Contains("Monthly usage", Assert.Throws<WorkspaceException>(() => s.Apply(Run(next))).Message);
        Assert.Equal("claimed", s.Attempt(next.Id).Phase);
        Assert.Equal(1, Store().Usage().Current.Jobs);
        Assert.All(MeasuredQueueChecks.Items<CompanyBudget>(s, "budgets"), b => Assert.Equal(0, b.Reserved));
    }

    [Fact]
    public async Task ConcurrentAdmissionsCannotOverspendTheMonthlyJobLimit()
    {
        var s = Store();
        Configure(s, 1, 1_000_000);
        var a = Prepare(s, MeasuredQueueChecks.Seed(s));
        var b = Prepare(s, MeasuredQueueChecks.Seed(s));
        var results = await Task.WhenAll(
            new[] { a, b }.Select(attempt =>
                Task.Run(
                    () =>
                    {
                        try
                        {
                            Store().Apply(Run(attempt));
                            return true;
                        }
                        catch (WorkspaceException)
                        {
                            return false;
                        }
                    },
                    TestContext.Current.CancellationToken
                )
            )
        );
        Assert.Single(results, success => success);
        Assert.Equal(1, s.Usage().Current.Jobs);
        Assert.Equal(14_000, s.Usage().Current.HeldTokens);
    }

    [Fact]
    public void MonthRolloverKeepsOutstandingHoldsAndLateUsageInItsOriginalMonth()
    {
        var s = Store();
        Configure(s, 1, 14_000);
        var a = Prepare(s, MeasuredQueueChecks.Seed(s));
        s.Apply(Run(a));
        _now = _now.AddMinutes(3);
        var later = MeasuredQueueChecks.Seed(s);
        var b = Prepare(s, later);
        Assert.Equal("2026-11", s.Usage().Current.Period);
        Assert.Equal(0, s.Usage().Current.Jobs);
        Assert.Equal(1, s.Usage().Current.CarriedJobs);
        Assert.Equal(14_000, s.Usage().Current.CarriedTokens);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(b)));
        Finish(s, a);
        Assert.Equal(30, s.Usage("2026-10").Selected.Tokens);
        Assert.Equal(0, s.Usage().Current.Tokens);
        Assert.Equal(0, s.Usage().Current.CarriedJobs);
        s.Apply(Run(b));
        Finish(s, b);
        Assert.Equal(1, s.Usage().Current.Jobs);
        Assert.Equal(30, s.Usage().Current.Tokens);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), s.Usage().Current.ResetsAt);
    }

    [Fact]
    public void UnknownAndObservedOverageRetainTheLargerHoldAcrossRestart()
    {
        var s = Store();
        var a = Prepare(s, MeasuredQueueChecks.Seed(s));
        s.Apply(Run(a));
        s.RejectResponse(
            a.Id,
            new ModelTransportException(
                "Fictional overage",
                40_000,
                "fictional-overage",
                new(39_990, 10, BilledUsd: 0.01m),
                accountingViolation: true
            )
        );
        Assert.Equal("unknown", s.Attempt(a.Id).Phase);
        Assert.Equal(40_000, Store().Usage().Current.HeldTokens);
        Assert.Equal(0, s.Usage().Current.Tokens);
        Assert.Equal(0, s.Usage().Current.ReportedUsd);
        Assert.Equal(1, s.Usage().Current.ProvisionalCosts);
        _now = _now.AddMonths(1);
        Assert.Equal(40_000, Store().Usage().Current.CarriedTokens);
        Assert.Equal(1, Store().Usage().Current.CarriedJobs);
    }

    [Fact]
    public void PriceSnapshotsStayStableAndProviderChargesAreSeparateFromEstimates()
    {
        var s = Store(price: new(2, 10));
        var task = MeasuredQueueChecks.Seed(s);
        var a = Prepare(s, task);
        s.Apply(Run(a));
        Finish(s, a);
        Assert.Equal(0.00014m, s.Usage().Current.EstimatedUsd);
        Assert.Equal(0.00014m, Store(price: new(999, 999)).Usage().Current.EstimatedUsd);
        var b = Prepare(s, task);
        s.Apply(Run(b));
        Finish(s, b, 0.003m);
        Assert.Equal(0.003m, s.Usage().Current.ReportedUsd);
        Assert.Equal(0.00014m, s.Usage().Current.EstimatedUsd);
        Assert.Equal(0, s.Usage().Current.UnpricedJobs);
        var other = Store("co-unpriced");
        var c = Prepare(other, MeasuredQueueChecks.Seed(other));
        other.Apply(Run(c));
        Finish(other, c);
        Assert.Equal(1, other.Usage().Current.UnpricedJobs);
        Assert.Equal(0, other.Usage().Current.EstimatedUsd);
        Assert.Equal(2, s.Usage().Current.Jobs);
    }

    [Fact]
    public void OwnerPolicyChangesAreVersionedAndPauseDoesNotEraseOrCancelWork()
    {
        var s = Store();
        var task = MeasuredQueueChecks.Seed(s);
        var a = Prepare(s, task);
        s.Apply(Run(a));
        var policy = Configure(s, 0, 0, true);
        Assert.True(s.IsCurrent(a.Id));
        Finish(s, a);
        Assert.Equal(30, s.Usage().Current.Tokens);
        Assert.Throws<WorkspaceException>(() =>
            s.Apply(
                C("usage.configure") with
                {
                    ExpectedVersion = policy.Version - 1,
                    Body = CompanyJson.Encode(new CompanyUsageLimits()),
                }
            )
        );
        var b = Prepare(s, task);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(b)));
        Configure(s, 2, 14_030);
        s.Apply(Run(b));
        Assert.Equal(2, s.Usage().Current.Jobs);
        Assert.Equal(14_000, s.Usage().Current.HeldTokens);
        s.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = "approver" });
        var member = s.SignIn("fictional-usage-reviewer", "reviewer@example.invalid", true, "Fictional Reviewer")!;
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => s.Apply(C("usage.configure"), member.Subject, out _)).Status
        );
        Assert.Throws<WorkspaceException>(() => Configure(s, -1, 10));
    }

    [Fact]
    public void EarlierUndatedReservationsRemainVisibleAndConstrainNewAdmissions()
    {
        var s = Store();
        var a = Prepare(s, MeasuredQueueChecks.Seed(s));
        s.Apply(Run(a));
        using (var db = new StoreConnection(s.Database.Open(), s.Database))
            db.Execute("DELETE FROM co_usage_entries WHERE company_id=$0", s.CompanyId);
        var legacy = s.Usage("earlier");
        Assert.Equal(1, legacy.EarlierJobs);
        Assert.Null(Assert.Single(legacy.Entries).StartedAt);
        Assert.Null(legacy.Entries[0].Price);
        Assert.Equal(14_000, legacy.Current.CarriedTokens);
        Configure(s, 1, 14_000);
        var b = Prepare(s, MeasuredQueueChecks.Seed(s));
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(b)));
        Finish(s, a);
        Assert.Equal(30, s.Usage("earlier").Selected.Tokens);
        Assert.Equal(0, s.Usage().Current.CarriedTokens);
        s.Apply(Run(b));
        Assert.Equal(1, s.Usage().Current.Jobs);
    }

    [Fact]
    public void PreHireDraftsShareTheSameMonthlyAdmissionAndSettlement()
    {
        var s = Store();
        Configure(s, 1, 26_000);
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
            "owner@example.invalid",
            identity.Version,
            identity.Website!,
            identity.Name,
            "Fictional paper kits.",
            false
        );
        var draft = Apply<CompanyBusinessDraft>(
            s,
            C("business.draft.prepare") with
            {
                ExpectedVersion = identity.Version,
                Digest = identity.Digest,
                Evidence = [source.Id],
                CreditAllowance = 1,
            }
        );
        s.Apply(
            C("business.draft.run") with
            {
                Id = draft.Id,
                ExpectedVersion = draft.Version,
                Digest = draft.Job.Digest,
                Decision = "accept-soft-limits",
            }
        );
        Assert.Equal("profile-draft", Assert.Single(s.Usage().Entries).Kind);
        s.FinishBusinessDraft(
            draft.Id,
            new("Invalid fictional result", 100, "fictional-profile-receipt", new(80, 20, BilledUsd: 0.001m))
        );
        Assert.Equal("invalid", s.BusinessDraft(draft.Id).State);
        Assert.Equal(100, s.Usage().Current.Tokens);
        Assert.Equal(0.001m, s.Usage().Current.ReportedUsd);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(Prepare(s, MeasuredQueueChecks.Seed(s)))));
    }

    [Fact]
    public void UsageIsTenantScopedAndMigrationPreservesEarlierData()
    {
        var s = Store();
        var employee = MeasuredQueueChecks.Seed(s).EmployeeId;
        string[] hashes;
        using (var db = new StoreConnection(s.Database.Open(), s.Database))
        {
            hashes = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_decisions; DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE IF EXISTS co_document_retention; DROP TABLE IF EXISTS co_sandbox_jobs; DROP TABLE IF EXISTS co_compute_policy; DROP TABLE IF EXISTS co_workspace_leases; DROP TABLE co_usage_entries; DROP TABLE co_usage_policy; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=19;"
            );
        }
        var reopened = Store();
        Assert.Contains(MeasuredQueueChecks.Items<CompanyEmployee>(reopened, "employees"), e => e.Id == employee);
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal(
            hashes,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            )
        );
        Configure(reopened, 1, 14_000, true);
        var other = Store("co-other-usage");
        Assert.False(other.Usage().Policy.Limits.Paused);
        Assert.Empty(other.Usage().Entries);
        Assert.Throws<WorkspaceException>(() => other.Usage("2026-99"));
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.Usage(before: "foreign-receipt")).Status);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
