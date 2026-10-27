using HireZero.Core;
using HireZero.Integrations.Sandbox;
using Microsoft.Data.Sqlite;

public sealed class CompanyScheduledBrowserTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("recurring-browser-");
    private readonly BrowserFixtureExecutor _executor = new();
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-26T08:00:00Z");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore store, CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            companyId: company
        )
        {
            Clock = () => _now,
            BrowserRuntimeIdentity = _executor.Identity,
            BrowserExecutionAvailable = () => _executor.ExecutionEnabled,
        };

    private CompanyBrowserJob Seed(CompanyStore store)
    {
        var task = MeasuredQueueChecks.Seed(store);
        var facts = A<CompanyKnowledge>(
            store,
            C("knowledge.save") with
            {
                Body = "Fictional paper studio. Private website reviews only.",
            }
        );
        store.Apply(
            C("knowledge.review") with
            {
                Id = facts.Id,
                ExpectedVersion = facts.Version,
                Digest = facts.Digest,
                Decision = "approve",
            }
        );
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = store.ComputeUsage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyComputeLimits(10, 1800, false)),
            }
        );
        return A<CompanyBrowserJob>(
            store,
            C("browser.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = CompanyJson.Encode(
                    new CompanyBrowserInput(
                        "Fictional landing page",
                        ["https://paper.example.invalid"],
                        [new("navigate", "https://paper.example.invalid/services"), new("read", "body")],
                        new(5)
                    )
                ),
            }
        );
    }

    private CompanySchedule Draft(CompanyStore store, CompanyBrowserJob template, int count = 2) =>
        A<CompanySchedule>(
            store,
            C("schedule.save") with
            {
                Body = CompanyJson.Encode(
                    new CompanyScheduleInput(
                        "Fictional recurring website review",
                        "browser-task",
                        "daily",
                        "UTC",
                        "2026-10-27",
                        "09:00",
                        count,
                        EmployeeId: template.EmployeeId,
                        BrowserJobId: template.Id
                    )
                ),
            }
        );

    private static CompanySchedule Review(
        CompanyStore store,
        CompanySchedule schedule,
        string decision = "authorize-recurring-browsers"
    ) =>
        A<CompanySchedule>(
            store,
            C("schedule.review") with
            {
                Id = schedule.Id,
                ExpectedVersion = schedule.Version,
                Digest = schedule.Digest,
                Decision = decision,
            }
        );

    private CompanyBrowserJob Due(CompanyStore store, CompanySchedule schedule)
    {
        _now = store.ScheduleDetail(schedule.Id).Schedule.NextRunAt!.Value;
        return store.StartScheduledBrowser(Assert.IsType<CompanyScheduleRun>(store.ClaimDueSchedule()).Id);
    }

    private static void Accept(CompanyStore store, CompanyBrowserJob job)
    {
        var capture = Assert.Single(job.Execution!.Result!.Captures);
        var artifact = A<CompanyArtifact>(
            store,
            C("browser.output.prepare") with
            {
                Id = job.Id,
                ExpectedVersion = job.Version,
                Digest = job.ResultDigest,
                Decision = "prepare-review",
                Body = CompanyJson.Encode(new CompanyBrowserArtifactInput(capture.Step, capture.Text)),
            }
        );
        store.Apply(
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
        store.Apply(C("task.complete") with { Id = job.TaskId, ExpectedVersion = store.Work(job.TaskId).Version });
        Assert.Equal("completed", store.Work(job.TaskId).State);
    }

    [Fact]
    public void ExactItineraryNeedsSeparateRecurringConsentAndStaysWithinItsCompany()
    {
        var store = Store();
        var template = Seed(store);
        var draft = Draft(store, template);
        Assert.Equal(
            CompanyJson.Encode(template.Request.Steps),
            CompanyJson.Encode(draft.Authority.Browser!.Itinerary.Steps)
        );
        Assert.Equal(template.Request.Screenshots, draft.Authority.Browser.Itinerary.Screenshots);
        Assert.Equal(0, _executor.Executions);
        Assert.Null(store.ClaimDueSchedule());
        Assert.Throws<WorkspaceException>(() => Review(store, draft, "authorize-recurring-scripts"));
        Assert.Throws<WorkspaceException>(() => Review(store, draft, "authorize-recurring-source-reads"));
        var other = Store("co-fictional-other");
        Assert.Empty(other.BrowserScheduleTemplates());
        Assert.Throws<WorkspaceException>(() => Draft(other, template));
        Assert.DoesNotContain(
            "browserJobId",
            CompanyJson.Encode(
                new CompanyScheduleInput("legacy", "source-refresh", "daily", "UTC", "2026-10-27", "09:00", 1)
            )
        );
        Assert.DoesNotContain(
            "browser",
            CompanyJson.Encode(new CompanyScheduleAuthority("legacy", null, null, null, new("", "", "", "", "", "")))
        );
    }

    [Fact]
    public async Task TwoOccurrencesKeepTheirExactItineraryAndRequireSeparateResultReview()
    {
        var store = Store();
        var template = Seed(store);
        var schedule = Review(store, Draft(store, template));
        var first = Due(store, schedule);
        Assert.Equal(schedule.Id, first.Schedule!.ScheduleId);
        Assert.Equal(17, store.ComputeUsage().HeldSeconds);
        Assert.Null(store.Claim(first.EmployeeId, true, first.TaskId));
        Assert.Throws<WorkspaceException>(() => store.StartScheduledBrowser(first.Schedule.RunId));
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.retry") with { Id = first.TaskId, ExpectedVersion = store.Work(first.TaskId).Version })
        );
        first = await new CompanyBrowserRunner(store, _executor).RunAsync(first.Id, Token);
        Assert.Equal("completed", store.ScheduleDetail(schedule.Id).Runs[0].State);
        Assert.Equal("awaiting_review", store.Work(first.TaskId).State);
        Accept(store, first);
        var second = Due(store, schedule);
        Assert.NotEqual(first.Request.JobId, second.Request.JobId);
        Assert.Equal(CompanyJson.Encode(first.Request.Steps), CompanyJson.Encode(second.Request.Steps));
        second = await new CompanyBrowserRunner(store, _executor).RunAsync(second.Id, Token);
        Assert.Equal("completed", Store().ScheduleDetail(schedule.Id).Schedule.State);
        Accept(store, second);
        Assert.Equal(2, _executor.Executions);
        Assert.Equal(34, store.ComputeUsage().AllocatedSeconds);
        Assert.Null(store.ClaimDueSchedule());
    }

    [Fact]
    public async Task PausingWithdrawsActiveAuthorityAndCannotResumeUntilResourcesAreReleased()
    {
        var store = Store();
        var schedule = Review(store, Draft(store, Seed(store)));
        var job = Due(store, schedule);
        _executor.Mode = "wait";
        var running = new CompanyBrowserRunner(store, _executor).RunAsync(job.Id, Token);
        var until = DateTimeOffset.UtcNow.AddSeconds(5);
        while (_executor.Executions == 0 && DateTimeOffset.UtcNow < until)
            await Task.Delay(20, Token);
        Assert.Equal(1, _executor.Executions);
        var paused = Review(store, store.ScheduleDetail(schedule.Id).Schedule, "pause");
        Assert.Throws<WorkspaceException>(() => Review(store, paused));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal("withheld", result.State);
        Assert.Null(result.Execution!.Result);
        Assert.True(result.Release!.Confirmed);
        Assert.Equal("stopped", store.ScheduleDetail(schedule.Id).Runs[0].State);
        Review(store, store.ScheduleDetail(schedule.Id).Schedule);
        Assert.Null(store.ClaimDueSchedule());
        Assert.Equal(1, _executor.Executions);
    }

    [Fact]
    public async Task UncertainCleanupSurvivesRestartAndRecoveryNeverRevisitsTheWebsite()
    {
        var store = Store();
        var template = Seed(store);
        var schedule = Review(store, Draft(store, template));
        var other = Review(store, Draft(store, template));
        var first = Due(store, schedule);
        _executor.Mode = "unknown";
        _executor.ReleaseFailures = 1;
        await new CompanyBrowserRunner(store, _executor).RunAsync(first.Id, Token);
        var reloaded = Store();
        reloaded.Recover();
        Assert.Equal("running", reloaded.ScheduleDetail(schedule.Id).Runs[0].State);
        Assert.Equal(17, reloaded.ComputeUsage().HeldSeconds);
        Assert.Equal(other.Id, Assert.IsType<CompanyScheduleRun>(reloaded.ClaimDueSchedule()).ScheduleId);
        await new CompanyBrowserRunner(reloaded, _executor).RecoverAsync(first.Id, Token);
        Assert.Equal("unknown", reloaded.ScheduleDetail(schedule.Id).Runs[0].State);
        Assert.Equal("blocked", reloaded.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Equal(0, reloaded.ComputeUsage().HeldSeconds);
        Assert.Equal(1, _executor.Executions);
    }

    [Fact]
    public async Task StartupReconcilesAnUnsentOccurrenceWithoutDispatchingItAgain()
    {
        var store = Store();
        var schedule = Review(store, Draft(store, Seed(store)));
        var job = Due(store, schedule);
        var reloaded = Store();
        reloaded.Recover();
        var result = await new CompanyBrowserRunner(reloaded, _executor).RecoverAsync(job.Id, Token);
        Assert.Equal("not-sent", result.State);
        Assert.Equal(0, result.AllocatedSeconds);
        Assert.Equal(0, _executor.Executions);
        Assert.Equal("failed", reloaded.ScheduleDetail(schedule.Id).Runs[0].State);
        Assert.Equal(1, reloaded.ScheduleDetail(schedule.Id).Schedule.Consumed);
    }

    [Fact]
    public async Task SchedulerAdmissionIsAtomicAndNeverFallsBackToAModelRequest()
    {
        var store = Store();
        var schedule = Review(store, Draft(store, Seed(store)));
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = store.ComputeUsage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyComputeLimits(0, 0, false)),
            }
        );
        _now = schedule.NextRunAt!.Value;
        Assert.True(await new CompanyScheduler(store, new UnavailableModelProvider()).TickSchedulesAsync(Token));
        var run = Assert.Single(store.ScheduleDetail(schedule.Id).Runs);
        Assert.Equal("failed", run.State);
        Assert.Null(run.TaskId);
        Assert.Null(run.BrowserId);
        Assert.Equal(0, store.ComputeUsage().HeldSeconds);
        Assert.Equal(0, _executor.Executions);
    }

    [Fact]
    public void DisabledRuntimeBlocksFutureVisitsAndLateSlotsNeverCreateACatchUpBurst()
    {
        var store = Store();
        var template = Seed(store);
        var schedule = Review(store, Draft(store, template, 3));
        _now = DateTimeOffset.Parse("2026-10-29T09:15:00Z");
        var run = Assert.IsType<CompanyScheduleRun>(store.ClaimDueSchedule());
        Assert.Equal(2, run.Slot);
        Assert.Equal(2, run.Missed);
        store.Apply(C("browser.revoke") with { Id = template.Id, ExpectedVersion = template.Version });
        Assert.Equal("running", store.StartScheduledBrowser(run.Id).State);
        Assert.Empty(store.BrowserScheduleTemplates());
        _executor.ExecutionEnabled = false;
        Assert.False(store.BrowserAuthorized(store.ScheduleDetail(schedule.Id).Runs[0].BrowserId!));
        Assert.Equal(0, _executor.Executions);
    }

    [Fact]
    public void RuntimeWithdrawalBlocksApprovalAndFutureSlotsWithoutConsumingAnOccurrence()
    {
        var store = Store();
        var draft = Draft(store, Seed(store));
        _executor.ExecutionEnabled = false;
        Assert.Throws<WorkspaceException>(() => Review(store, draft));
        _executor.ExecutionEnabled = true;
        var schedule = Review(store, draft);
        _now = schedule.NextRunAt!.Value;
        _executor.ExecutionEnabled = false;
        Assert.Null(store.ClaimDueSchedule());
        var stopped = store.ScheduleDetail(schedule.Id);
        Assert.Equal("blocked", stopped.Schedule.State);
        Assert.Equal(0, stopped.Schedule.Consumed);
        Assert.Empty(stopped.Runs);
        Assert.Equal(0, _executor.Executions);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
