using System.Text.Json;
using HireZero.Core;
using HireZero.Tools.Web;
using Microsoft.Data.Sqlite;

public sealed partial class CompanyScheduleTests : IDisposable
{
    private const string Owner = "owner@example.invalid",
        Runtime = "fictional-schedule-runtime";
    private readonly TemporaryRoot _root = new("company-schedules-");
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");

    private CompanyStore Store(
        string company = CompanyStore.DefaultCompanyId,
        Func<CompanyModelRouting>? routing = null,
        Func<string, CompanyModelTerms?>? terms = null
    ) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: Runtime,
            companyId: company
        )
        {
            Clock = () => _now,
            CurrentModelRouting = routing,
            MeasuredTerms = terms ?? (_ => new("host", "credits")),
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    private CompanyScheduleInput Definition(string? employee = null) =>
        new(
            "Weekly content outline",
            "employee-task",
            "daily",
            "UTC",
            "2026-10-26",
            "09:00",
            2,
            employee,
            "Prepare three modest content ideas from approved company knowledge.",
            1
        );

    private static CompanyCommand Review(CompanySchedule s, string? decision = null) =>
        C("schedule.review") with
        {
            Id = s.Id,
            ExpectedVersion = s.Version,
            Digest = s.Digest,
            Decision =
                decision
                ?? (
                    s.Definition.Kind == "employee-task"
                        ? "authorize-recurring-soft-limits"
                        : "authorize-recurring-source-reads"
                ),
        };

    private static string Employee(CompanyStore s)
    {
        var work = MeasuredQueueChecks.Seed(s);
        var facts = A<CompanyKnowledge>(
            s,
            C("knowledge.save") with
            {
                Body = "Fictional paper studio; only internal drafts, no publishing or unsupported claims.",
            }
        );
        s.Apply(
            C("knowledge.review") with
            {
                Id = facts.Id,
                ExpectedVersion = facts.Version,
                Digest = facts.Digest,
                Decision = "approve",
            }
        );
        return work.EmployeeId;
    }

    private CompanySchedule Draft(CompanyStore s, CompanyScheduleInput input) =>
        A<CompanySchedule>(s, C("schedule.save") with { Body = CompanyJson.Encode(input) });

    private static CompanySchedule Approve(CompanyStore s, CompanySchedule draft) =>
        A<CompanySchedule>(s, Review(draft));

    private static ModelReply Reply =>
        new(
            "Three fictional themes: paper folding, studio process and consent-based workshop stories.",
            30,
            "fictional-recurring-receipt",
            new(20, 10)
        );

    private sealed class Model(
        Func<ModelCall, CancellationToken, Task<ModelReply>>? reply = null,
        CompanyMeasuredPolicy? policy = null
    ) : IModelProvider, IMeasuredModelProvider
    {
        public bool Qualified => false;
        public string Status => "Fictional measured provider only";
        public string RuntimeIdentity => Runtime;
        public int Calls;
        public ModelCall? Last;

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            Calls++;
            Last = call;
            Assert.Equal("dispatched", job.State);
            Assert.Equal(policy ?? CompanyMeasuredPolicy.Conversation, job.Policy);
            Assert.Equal(job.ContextDigest, CompanyJson.Digest(call.Context));
            return reply?.Invoke(call, token) ?? Task.FromResult(Reply);
        }
    }

    [Fact]
    public void LocalSchedulesHandleClockGapsAndRepeatedHoursOnce()
    {
        Assert.Equal("America/Denver", CompanyScheduleCalendar.CanonicalZone("Mountain Standard Time"));
        var spring = Definition() with { TimeZone = "America/Denver", StartDate = "2026-03-08", LocalTime = "02:30" };
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T09:00:00Z"), CompanyScheduleCalendar.Slot(spring, 0));
        Assert.Equal(DateTimeOffset.Parse("2026-03-09T08:30:00Z"), CompanyScheduleCalendar.Slot(spring, 1));
        var autumn = spring with { StartDate = "2026-11-01", LocalTime = "01:30" };
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T07:30:00Z"), CompanyScheduleCalendar.Slot(autumn, 0));
        Assert.Equal(
            DateTimeOffset.Parse("2026-11-08T08:30:00Z"),
            CompanyScheduleCalendar.Slot(autumn with { Cadence = "weekly" }, 1)
        );
        Assert.Throws<WorkspaceException>(() =>
            CompanyScheduleCalendar.Slot(spring with { TimeZone = "Fictional/Unknown" }, 0)
        );
        Assert.Throws<WorkspaceException>(() => CompanyScheduleCalendar.Slot(spring with { LocalTime = "25:00" }, 0));
    }

    [Fact]
    public void SchedulesRequireExactOwnerApprovalAndRemainTenantScoped()
    {
        var s = Store();
        var employee = Employee(s);
        var draft = Draft(s, Definition(employee));
        Assert.Null(s.ClaimDueSchedule());
        Assert.Contains("Fictional paper studio", draft.Authority.Context.CompanyFacts);
        Assert.Throws<WorkspaceException>(() => s.Apply(Review(draft) with { Digest = "wrong" }));
        s.Apply(C("member.invite") with { Body = "viewer@example.invalid", Decision = "viewer" });
        var viewer = s.SignIn("fictional-viewer", "viewer@example.invalid", true, "Fictional Viewer")!;
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => s.Apply(Review(draft), viewer.Subject, out _)).Status
        );
        var command = Review(draft);
        var first = s.Apply(command);
        Assert.Equal(first, s.Apply(command));
        Assert.Equal(
            1,
            MeasuredQueueChecks.Items<CompanyThread>(s, "threads").Count(t => t.Title.StartsWith("Recurring:"))
        );
        var other = Store("co-other-schedules");
        Assert.Empty(other.Schedules());
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.ScheduleDetail(draft.Id)).Status);
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.Apply(Review(draft))).Status);
        Assert.Throws<WorkspaceException>(() => Draft(s, Definition(employee) with { Occurrences = 32 }));
        Assert.True(s.VerifyAudit().Intact);
    }

    [Fact]
    public async Task EachApprovedSlotProducesOnePrivateResultAndSettlesTheSharedScheduleBudget()
    {
        var s = Store();
        var employee = Employee(s);
        var schedule = Approve(s, Draft(s, Definition(employee)));
        var model = new Model();
        var scheduler = new CompanyScheduler(s, model);
        Assert.False(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        _now = schedule.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.False(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, model.Calls);
        Assert.Empty(model.Last!.Context.History);
        var first = s.ScheduleDetail(schedule.Id);
        Assert.Equal("completed", Assert.Single(first.Runs).State);
        Assert.Equal("active", first.Schedule.State);
        Assert.Equal("completed", s.Work(first.Runs[0].TaskId!).State);
        _now = first.Schedule.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, model.Calls);
        Assert.Equal("completed", s.ScheduleDetail(schedule.Id).Schedule.State);
        var budget = MeasuredQueueChecks
            .Items<CompanyBudget>(s, "budgets")
            .Single(b => b.Id == "schedule:" + schedule.Id);
        Assert.Equal(60, budget.Used);
        Assert.Equal(0, budget.Reserved);
        Assert.Equal(2 * CompanyMeasuredPolicy.Conversation.PlanningTokens, budget.Limit);
        Assert.False(
            await new CompanyScheduler(Store(), model).TickSchedulesAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public void MissedSlotsConsumeTheFiniteApprovalWithoutCatchUpBursts()
    {
        var s = Store();
        var schedule = Approve(s, Draft(s, Definition(Employee(s)) with { Occurrences = 5 }));
        _now = schedule.NextRunAt!.Value.AddDays(3).AddMinutes(30);
        var run = s.ClaimDueSchedule()!;
        Assert.Equal(3, run.Missed);
        Assert.Equal(3, run.Slot);
        Assert.Equal("claimed", run.State);
        Assert.Null(s.ClaimDueSchedule());
        s.Recover();
        Assert.Equal("blocked", s.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Null(s.ClaimDueSchedule());
        var resumed = Approve(s, s.ScheduleDetail(schedule.Id).Schedule);
        _now = resumed.NextRunAt!.Value.AddHours(2);
        var missed = s.ClaimDueSchedule()!;
        Assert.Equal("missed", missed.State);
        Assert.Equal(1, missed.Missed);
        Assert.Equal("completed", s.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Null(s.ClaimDueSchedule());
    }

    [Fact]
    public async Task ChangedContextOrRevokedOwnerStopsFutureScheduledDispatch()
    {
        var s = Store();
        var employee = Employee(s);
        var schedule = Approve(s, Draft(s, Definition(employee)));
        s.Apply(
            C("notes.save") with
            {
                EmployeeId = employee,
                Body = "Owner corrected the current marketing priorities.",
            }
        );
        _now = schedule.NextRunAt!.Value;
        var model = new Model();
        Assert.False(await new CompanyScheduler(s, model).TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, model.Calls);
        Assert.Equal("blocked", s.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Throws<WorkspaceException>(() => s.Apply(Review(s.ScheduleDetail(schedule.Id).Schedule)));
        var other = Store("co-revoked-schedule");
        var secondEmployee = Employee(other);
        other.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        var actor = other.SignIn("fictional-second", "second-owner@example.invalid", true, "Second Owner")!;
        var draft = Draft(other, Definition(secondEmployee) with { StartDate = "2026-10-27" });
        var active = CompanyJson.Decode<CompanySchedule>(other.Apply(Review(draft), actor.Subject, out _));
        var member = other.Members().Single(m => m.Id == actor.Subject);
        other.Apply(C("member.revoke") with { Id = member.Id, ExpectedVersion = member.Version });
        _now = active.NextRunAt!.Value;
        Assert.Null(other.ClaimDueSchedule());
        Assert.Equal("blocked", other.ScheduleDetail(active.Id).Schedule.State);
    }

    [Fact]
    public async Task UnknownProviderOutcomeIsRetainedAndNeverAutomaticallyRetried()
    {
        var s = Store();
        var schedule = Approve(s, Draft(s, Definition(Employee(s))));
        var model = new Model((_, _) => throw new ModelTransportException("Fictional lost provider receipt"));
        _now = schedule.NextRunAt!.Value;
        Assert.True(await new CompanyScheduler(s, model).TickSchedulesAsync(TestContext.Current.CancellationToken));
        var detail = s.ScheduleDetail(schedule.Id);
        Assert.Equal("unknown", Assert.Single(detail.Runs).State);
        Assert.Equal("blocked", detail.Schedule.State);
        Assert.Throws<WorkspaceException>(() => s.Apply(Review(detail.Schedule)));
        var reopened = Store();
        reopened.Recover();
        _now = _now.AddDays(5);
        Assert.False(
            await new CompanyScheduler(reopened, model).TickSchedulesAsync(TestContext.Current.CancellationToken)
        );
        Assert.Equal(1, model.Calls);
        Assert.True(
            MeasuredQueueChecks
                .Items<CompanyBudget>(s, "budgets")
                .Single(b => b.Id == "schedule:" + schedule.Id)
                .Reserved > 0
        );
    }

    [Fact]
    public async Task PausingDuringExecutionWithholdsLateContentButAccountsForUsage()
    {
        var s = Store();
        var schedule = Approve(s, Draft(s, Definition(Employee(s))));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new Model(
            (_, _) =>
            {
                started.SetResult();
                return finish.Task;
            }
        );
        _now = schedule.NextRunAt!.Value;
        var running = new CompanyScheduler(s, model).TickSchedulesAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var paused = A<CompanySchedule>(s, Review(s.ScheduleDetail(schedule.Id).Schedule, "pause"));
        Assert.Throws<WorkspaceException>(() => s.Apply(Review(paused)));
        finish.SetResult(Reply);
        await running;
        var run = Assert.Single(s.ScheduleDetail(schedule.Id).Runs);
        Assert.Equal("stopped", run.State);
        Assert.Equal("withheld", s.Attempt(run.AttemptId!).Phase);
        Assert.Equal(30, s.Attempt(run.AttemptId!).ReportedUsage);
        Assert.Equal(
            0,
            MeasuredQueueChecks
                .Items<CompanyBudget>(s, "budgets")
                .Single(b => b.Id == "schedule:" + schedule.Id)
                .Reserved
        );
        Assert.Equal(
            0,
            JsonSerializer
                .SerializeToElement(s.Thread(paused.Definition.EmployeeId!, paused.ThreadId!), CompanyJson.Options)
                .GetProperty("messages")
                .GetArrayLength()
        );
    }

    private static (CompanyBusinessProfile Profile, CompanyBusinessSource Source) Business(CompanyStore s)
    {
        var identity = A<CompanyBusinessIdentity>(
            s,
            C("business.identify") with
            {
                Body = CompanyJson.Encode(
                    new CompanyBusinessInput("Fictional Studio", "https://paper.example.invalid/", "Plan a launch")
                ),
            }
        );
        identity = A<CompanyBusinessIdentity>(
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
            "We make fictional paper kits.",
            false
        );
        var fields = CompanyStore
            .BusinessProfileFields.Select(k =>
                k == "business"
                    ? new CompanyProfileField(k, "Paper kits", "sourced", [new(source.Id, source.Text)])
                    : new CompanyProfileField(k, "Unknown", "unknown", [])
            )
            .ToArray();
        var p = A<CompanyBusinessProfile>(
            s,
            C("business.profile.save") with
            {
                Body = CompanyJson.Encode(new CompanyProfileInput(identity.Version, fields)),
            }
        );
        return (
            A<CompanyBusinessProfile>(
                s,
                C("business.profile.review") with
                {
                    Id = p.Id,
                    ExpectedVersion = p.Version,
                    Digest = p.Digest,
                    Decision = "approve",
                }
            ),
            source
        );
    }

    [Fact]
    public async Task ScheduledSourceChecksUseExistingReadGuardsAndPausingWithholdsLateEvidence()
    {
        var s = Store();
        var (p, source) = Business(s);
        var schedule = Approve(
            s,
            Draft(s, Definition() with { Kind = "source-refresh", ProfileId = p.Id, SourceIds = [source.Id] })
        );
        var tools = new ToolRegistry(s);
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<WebPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        tools.Register(
            WebTools.Research,
            (request, _) =>
            {
                calls++;
                Assert.Equal(source.Url, request.Url);
                started.TrySetResult();
                return calls == 1 ? Task.FromResult(new WebPage(source.Url, "About", source.Text, false)) : finish.Task;
            }
        );
        var model = new Model();
        var scheduler = new CompanyScheduler(s, model)
        {
            RefreshScheduledSources = (c, owner, token) =>
                CompanyBusinessResearch.RunRefreshAsync(c, owner, s, tools, token),
        };
        _now = schedule.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, model.Calls);
        var first = s.ScheduleDetail(schedule.Id);
        Assert.Equal("completed", first.Runs[0].State);
        Assert.Equal("unchanged", s.BusinessFreshness(p.Id).Fields[0].Status);
        _now = first.Schedule.NextRunAt!.Value;
        var running = scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken);
        while (calls < 2)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        s.Apply(Review(s.ScheduleDetail(schedule.Id).Schedule, "pause"));
        finish.SetResult(new(source.Url, "About", "We now offer only workshops.", false));
        await running;
        Assert.Equal("unchanged", s.BusinessFreshness(p.Id).Fields[0].Status);
        Assert.Equal(2, calls);
        Assert.False(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ScheduleMigrationKeepsEarlierBriefsDocumentsAndChecksums()
    {
        var s = Store();
        var employee = Employee(s);
        string[] checksums;
        using (var db = new StoreConnection(s.Database.Open(), s.Database))
        {
            checksums = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_decisions; DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE IF EXISTS co_document_retention; DROP TABLE IF EXISTS co_sandbox_jobs; DROP TABLE IF EXISTS co_compute_policy; DROP TABLE IF EXISTS co_workspace_leases; DROP TABLE IF EXISTS co_usage_entries; DROP TABLE IF EXISTS co_usage_policy; DROP TABLE IF EXISTS co_memory_terms; DROP TABLE IF EXISTS co_memory; DROP TABLE IF EXISTS co_notice_reads; DROP TABLE IF EXISTS co_notice_deliveries; DROP TABLE IF EXISTS co_notice_preferences; DROP TABLE IF EXISTS co_verified_emails; DROP TABLE IF EXISTS co_notices; DROP TABLE co_schedule_runs; DROP TABLE co_schedules; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=16;"
            );
        }
        var reopened = Store();
        Assert.Empty(reopened.Schedules());
        Draft(reopened, Definition(employee));
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal(
            checksums,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            )
        );
    }

    [Fact]
    public async Task HostRestoresSchedulesInCompaniesThatNobodyHasOpenedAfterRestart()
    {
        var s = Store("co-unvisited-schedule");
        var scheduled = Approve(s, Draft(s, Definition(Employee(s)) with { Occurrences = 1 }));
        _now = scheduled.NextRunAt!.Value;
        var model = new Model();
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixture: model,
            fixtureUrl: "http://127.0.0.1:0",
            fixtureDatabase: s.Database,
            fixtureClock: () => _now
        )!;
        await app.StartAsync(TestContext.Current.CancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (s.ScheduleDetail(scheduled.Id).Schedule.State == "active" && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("completed", s.ScheduleDetail(scheduled.Id).Schedule.State);
        Assert.Equal(1, model.Calls);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecurringTextKeepsItsReviewedModelAndStopsWhenThatChoiceIsRemoved()
    {
        const string alternate = "typesafe/fictional-router";
        var routing = new CompanyModelRouting { AdditionalTextModels = [alternate] };
        var s = Store(
            routing: () => routing,
            terms: route => route == alternate ? new("openrouter", "usd-cents") : new("host", "credits")
        );
        var schedule = Approve(s, Draft(s, Definition(Employee(s)) with { ModelRoute = alternate }));
        Assert.Equal(alternate, schedule.Authority.Route);
        Assert.Equal(new CompanyModelTerms("openrouter", "usd-cents"), schedule.Authority.Terms);
        var model = new Model();
        using var scheduler = new CompanyScheduler(s, model);
        _now = schedule.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(alternate, model.Last!.ModelRoute);
        var detail = s.ScheduleDetail(schedule.Id);
        Assert.Equal(alternate, s.Work(detail.Runs[0].TaskId!).RequestedModelRoute);
        routing = new CompanyModelRouting();
        _now = detail.Schedule.NextRunAt!.Value;
        await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, model.Calls);
        Assert.Equal("blocked", s.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.True(s.VerifyAudit().Intact);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
