using HireZero.Core;
using HireZero.Integrations.Sandbox;
using Microsoft.Data.Sqlite;

public sealed class CompanyScheduledScriptTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("recurring-script-");
    private readonly SandboxFixtureExecutor _executor = new() { ExtendedEnabled = true };
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-26T08:00:00Z");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore store, CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));

    private CompanyStore Store(
        string company = CompanyStore.DefaultCompanyId,
        BillingFixtureProvider? billing = null
    ) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            companyId: company
        )
        {
            Clock = () => _now,
            SandboxRuntimeIdentity = _executor.Identity,
            SandboxExecutionAvailable = () => _executor.ExecutionEnabled,
            ExtendedSandboxAvailable = () => _executor.ExtendedScriptsAvailable,
            BillingConfiguration = billing is null ? null : new(billing.Identity, [billing.SelectedPlan]),
        };

    private (CompanySandboxJob Script, CompanyDocument Document) Seed(CompanyStore store, bool extended = false)
    {
        var task = MeasuredQueueChecks.Seed(store);
        var facts = A<CompanyKnowledge>(
            store,
            C("knowledge.save") with
            {
                Body = "Fictional paper studio. Internal reporting only.",
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
        var document = Document(store, "campaign,leads\nPaper,3");
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = store.ComputeUsage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyComputeLimits(10, 1800, false)),
            }
        );
        var script = A<CompanySandboxJob>(
            store,
            C("sandbox.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = CompanyJson.Encode(
                    new CompanySandboxInput(
                        "Fictional recurring report",
                        "console.log('Fictional');",
                        [new(document.Id, "rows.txt")],
                        ["report.txt"],
                        new(extended ? 180 : 5),
                        Tier: extended ? SandboxRequest.ExtendedTier : null
                    )
                ),
            }
        );
        return (script, document);
    }

    private static CompanyDocument Document(
        CompanyStore store,
        string text,
        CompanyDocument? previous = null,
        bool approve = true
    )
    {
        var document = A<CompanyDocument>(
            store,
            C("document.save") with
            {
                Body = CompanyJson.Encode(
                    new CompanyDocumentInput(
                        "Fictional campaign rows",
                        text,
                        SourceName: "rows.txt",
                        ReplacesId: previous?.Id
                    )
                ),
            }
        );
        if (previous is not null && store.Document(previous.Id) is { State: "approved" } old)
            store.Apply(
                C("document.review") with
                {
                    Id = old.Id,
                    ExpectedVersion = old.Version,
                    Digest = old.Digest,
                    Decision = "revoke",
                }
            );
        return approve
            ? A<CompanyDocument>(
                store,
                C("document.review") with
                {
                    Id = document.Id,
                    ExpectedVersion = document.Version,
                    Digest = document.Digest,
                    Decision = "approve",
                }
            )
            : document;
    }

    private CompanySchedule Draft(CompanyStore store, CompanySandboxJob script, int occurrences = 2) =>
        A<CompanySchedule>(
            store,
            C("schedule.save") with
            {
                Body = CompanyJson.Encode(
                    new CompanyScheduleInput(
                        "Fictional daily report",
                        "script-task",
                        "daily",
                        "UTC",
                        "2026-10-27",
                        "09:00",
                        occurrences,
                        EmployeeId: script.EmployeeId,
                        ScriptJobId: script.Id
                    )
                ),
            }
        );

    private static CompanySchedule Review(
        CompanyStore store,
        CompanySchedule schedule,
        string decision = "authorize-recurring-scripts"
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

    private CompanySandboxJob Due(CompanyStore store, CompanySchedule schedule)
    {
        _now = store.ScheduleDetail(schedule.Id).Schedule.NextRunAt!.Value;
        var run = Assert.IsType<CompanyScheduleRun>(store.ClaimDueSchedule());
        return store.StartScheduledSandbox(run.Id);
    }

    [Fact]
    public void DraftsPreserveOldContractsAndNeedTheSpecificRecurringScriptConsent()
    {
        var store = Store();
        var (script, _) = Seed(store);
        var draft = Draft(store, script);
        Assert.Equal(script.Request.Script, draft.Authority.Script!.Script);
        Assert.Equal(script.Documents[0].DocumentId, draft.Authority.Script.Documents[0].DocumentId);
        Assert.Equal(0, _executor.Executions);
        Assert.Null(store.ClaimDueSchedule());
        Assert.Throws<WorkspaceException>(() => Review(store, draft, "authorize-recurring-soft-limits"));
        Assert.Throws<WorkspaceException>(() => Draft(Store("fictional-other"), script));
        Assert.Equal("active", Review(store, draft).State);
        Assert.DoesNotContain(
            "scriptJobId",
            CompanyJson.Encode(
                new CompanyScheduleInput("Legacy", "employee-task", "daily", "UTC", "2026-10-27", "09:00", 1)
            )
        );
        Assert.DoesNotContain(
            "script",
            CompanyJson.Encode(new CompanyScheduleAuthority("legacy", null, null, null, new("", "", "", "", "", "")))
        );
    }

    [Fact]
    public async Task EachOccurrenceUsesTheLatestApprovedSelectedDocumentAndCanFinishThroughOwnerReview()
    {
        var store = Store();
        var (template, document) = Seed(store);
        var schedule = Review(store, Draft(store, template));
        var first = Due(store, schedule);
        Assert.Equal("campaign,leads\nPaper,3", first.Request.Inputs[0].Text);
        Assert.Equal(schedule.Id, first.Schedule!.ScheduleId);
        Assert.Equal(5, store.ComputeUsage().HeldSeconds);
        Assert.Null(store.Claim(first.EmployeeId, true, first.TaskId));
        Assert.Throws<WorkspaceException>(() => store.StartScheduledSandbox(first.Schedule.RunId));
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.retry") with { Id = first.TaskId, ExpectedVersion = store.Work(first.TaskId).Version })
        );
        first = await new CompanySandboxRunner(store, _executor).RunAsync(first.Id, Token);
        Assert.Equal("completed", store.ScheduleDetail(schedule.Id).Runs[0].State);
        Assert.Equal("awaiting_review", store.Work(first.TaskId).State);
        Assert.Equal("active", store.ScheduleDetail(schedule.Id).Schedule.State);
        var artifact = A<CompanyArtifact>(
            store,
            C("sandbox.output.prepare") with
            {
                Id = first.Id,
                ExpectedVersion = first.Version,
                Digest = first.ResultDigest,
                Decision = "prepare-review",
                Body = CompanyJson.Encode(new CompanySandboxArtifactInput("report.txt", first.Result!.Outputs[0].Text)),
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
        store.Apply(C("task.complete") with { Id = first.TaskId, ExpectedVersion = store.Work(first.TaskId).Version });
        Assert.Equal("completed", store.Work(first.TaskId).State);
        document = Document(store, "campaign,leads\nPaper,9", document);
        var second = Due(store, schedule);
        Assert.Equal(document.Version, second.Documents[0].Version);
        Assert.Equal(document.Id, second.Documents[0].DocumentId);
        Assert.Equal("campaign,leads\nPaper,9", second.Request.Inputs[0].Text);
        Assert.NotEqual(first.Request.JobId, second.Request.JobId);
        await new CompanySandboxRunner(store, _executor).RunAsync(second.Id, Token);
        Assert.Equal("completed", Store().ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Equal(2, _executor.Executions);
        Assert.Equal(10, store.ComputeUsage().AllocatedSeconds);
        Assert.Null(store.ClaimDueSchedule());
    }

    [Fact]
    public void DraftOrRevokedInputStopsTheScheduleWithoutReadingUnapprovedText()
    {
        var store = Store();
        var (template, document) = Seed(store);
        var schedule = Review(store, Draft(store, template));
        Document(store, "Fictional unapproved replacement", document, approve: false);
        _now = schedule.NextRunAt!.Value;
        Assert.Null(store.ClaimDueSchedule());
        Assert.Equal("blocked", store.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Equal(0, store.ComputeUsage().HeldSeconds);
        Assert.Equal(0, _executor.Executions);
    }

    [Fact]
    public async Task PauseCancelsActiveWorkAndResumingNeverReplaysTheConsumedOccurrence()
    {
        var store = Store();
        var (template, _) = Seed(store);
        var schedule = Review(store, Draft(store, template));
        var job = Due(store, schedule);
        _executor.Mode = "wait";
        var running = new CompanySandboxRunner(store, _executor).RunAsync(job.Id, Token);
        var limit = DateTimeOffset.UtcNow.AddSeconds(5);
        while (_executor.Executions == 0 && DateTimeOffset.UtcNow < limit)
            await Task.Delay(20, Token);
        Assert.Equal(1, _executor.Executions);
        var paused = Review(store, store.ScheduleDetail(schedule.Id).Schedule, "pause");
        Assert.Throws<WorkspaceException>(() => Review(store, paused));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal("withheld", result.State);
        Assert.Empty(result.Result!.Outputs);
        Assert.True(result.Release!.Confirmed);
        Assert.Equal("stopped", store.ScheduleDetail(schedule.Id).Runs[0].State);
        Review(store, store.ScheduleDetail(schedule.Id).Schedule);
        Assert.Null(store.ClaimDueSchedule());
        Assert.Equal(1, _executor.Executions);
        Assert.Equal(5, store.ComputeUsage().AllocatedSeconds);
    }

    [Fact]
    public async Task UnknownCleanupSurvivesRestartWithoutBlockingAnotherScheduleOrReplayingWork()
    {
        var store = Store();
        var (template, _) = Seed(store);
        var schedule = Review(store, Draft(store, template));
        var other = Review(store, Draft(store, template));
        var first = Due(store, schedule);
        _executor.Mode = "unknown";
        _executor.ReleaseFailures = 1;
        await new CompanySandboxRunner(store, _executor).RunAsync(first.Id, Token);
        var reloaded = Store();
        reloaded.Recover();
        Assert.Equal("running", reloaded.ScheduleDetail(schedule.Id).Runs[0].State);
        Assert.Equal(5, reloaded.ComputeUsage().HeldSeconds);
        var next = Assert.IsType<CompanyScheduleRun>(reloaded.ClaimDueSchedule());
        Assert.Equal(other.Id, next.ScheduleId);
        Assert.Equal(1, _executor.Executions);
        await new CompanySandboxRunner(reloaded, _executor).RecoverAsync(first.Id, Token);
        Assert.Equal("unknown", reloaded.ScheduleDetail(schedule.Id).Runs[0].State);
        Assert.Equal("blocked", reloaded.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Equal(0, reloaded.ComputeUsage().HeldSeconds);
        Assert.Equal(5, reloaded.ComputeUsage().AllocatedSeconds);
        Assert.Equal(1, _executor.Executions);
    }

    [Fact]
    public async Task StartupFencesAnApprovedButUnsentOccurrenceInsteadOfDispatchingItAgain()
    {
        var store = Store();
        var (template, _) = Seed(store);
        var schedule = Review(store, Draft(store, template));
        var job = Due(store, schedule);
        var reloaded = Store();
        reloaded.Recover();
        var result = await new CompanySandboxRunner(reloaded, _executor).RecoverAsync(job.Id, Token);
        Assert.Equal("not-sent", result.State);
        Assert.Equal(0, result.AllocatedSeconds);
        Assert.Equal(0, _executor.Executions);
        Assert.Equal("failed", reloaded.ScheduleDetail(schedule.Id).Runs[0].State);
        Assert.Equal(1, reloaded.ScheduleDetail(schedule.Id).Schedule.Consumed);
    }

    [Fact]
    public async Task AdmissionFailureConsumesOnlyTheDueSlotAndLeavesNoPartialTaskOrResourceHold()
    {
        var store = Store();
        var (template, _) = Seed(store);
        var schedule = Review(store, Draft(store, template));
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = store.ComputeUsage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyComputeLimits(0, 0, false)),
            }
        );
        _now = schedule.NextRunAt!.Value;
        var scheduler = new CompanyScheduler(store, new UnavailableModelProvider());
        Assert.True(await scheduler.TickSchedulesAsync(Token));
        var run = Assert.Single(store.ScheduleDetail(schedule.Id).Runs);
        Assert.Equal("failed", run.State);
        Assert.Null(run.TaskId);
        Assert.Null(run.SandboxId);
        Assert.Equal(0, store.ComputeUsage().HeldSeconds);
        Assert.Equal(0, _executor.Executions);
    }

    [Fact]
    public void LateSlotsNeverCatchUpAndTheReviewedScheduleIsSeparateFromItsOriginalProposal()
    {
        var store = Store();
        var (template, _) = Seed(store);
        var schedule = Review(store, Draft(store, template, 3));
        _now = DateTimeOffset.Parse("2026-10-29T09:15:00Z");
        var run = Assert.IsType<CompanyScheduleRun>(store.ClaimDueSchedule());
        Assert.Equal(2, run.Slot);
        Assert.Equal(2, run.Missed);
        Assert.Equal(3, store.ScheduleDetail(schedule.Id).Schedule.Consumed);
        store.Apply(C("sandbox.revoke") with { Id = template.Id, ExpectedVersion = template.Version });
        Assert.Equal("running", store.StartScheduledSandbox(run.Id).State);
        Assert.Empty(store.ScriptScheduleTemplates());
        Assert.Equal(0, _executor.Executions);
    }

    [Fact]
    public void AmbiguousApprovedReplacementsStopTheScheduleInsteadOfChoosingOne()
    {
        var store = Store();
        var (template, document) = Seed(store);
        var schedule = Review(store, Draft(store, template));
        Document(store, "Fictional replacement A", document);
        Document(store, "Fictional replacement B", document);
        _now = schedule.NextRunAt!.Value;
        Assert.Null(store.ClaimDueSchedule());
        Assert.Equal("blocked", store.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Equal(0, _executor.Executions);
    }

    [Fact]
    public async Task RecurringExtendedExecutionStillStopsWhenItsPaidEntitlementIsLost()
    {
        var provider = new BillingFixtureProvider
        {
            SelectedPlan = BillingFixtureProvider.ExtendedPlan,
            Clock = () => _now,
        };
        var store = Store(billing: provider);
        var checkout = A<CompanyBillingCheckout>(store, C("billing.prepare") with { Scope = provider.SelectedPlan.Id });
        store.Apply(
            C("billing.checkout") with
            {
                Id = checkout.Id,
                ExpectedVersion = checkout.Version,
                Digest = checkout.Digest,
                Decision = "approve-subscription",
            }
        );
        var billing = new CompanyBillingWorkflow(store, provider);
        Assert.True(await billing.DispatchAsync(checkout.Id, Token));
        provider.Change(checkout.Id, "paid");
        Assert.True(await billing.RefreshAsync(checkout.Id, Token));
        var (template, _) = Seed(store, extended: true);
        var schedule = Review(store, Draft(store, template));
        _now = schedule.NextRunAt!.Value;
        Assert.True(await billing.RefreshAsync(checkout.Id, Token));
        var job = Due(store, schedule);
        _executor.Mode = "wait";
        var running = new CompanySandboxRunner(store, _executor).RunAsync(job.Id, Token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (_executor.Executions == 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20, Token);
        Assert.Equal(1, _executor.Executions);
        provider.Change(checkout.Id, "past_due");
        Assert.True(await billing.RefreshAsync(checkout.Id, Token));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal("withheld", result.State);
        Assert.Empty(result.Result!.Outputs);
        Assert.True(result.Release!.Confirmed);
        Assert.Equal("blocked", store.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Equal(180, store.ComputeUsage().AllocatedSeconds);
        Assert.Equal(0, store.ComputeUsage().HeldSeconds);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
