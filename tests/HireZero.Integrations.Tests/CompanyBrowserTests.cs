using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Sandbox;
using Microsoft.Data.Sqlite;

public sealed partial class CompanyBrowserTests : IDisposable
{
    private const string Owner = "owner@example.invalid",
        Runtime = "fictional-browser-v1";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "browser-ledger-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 31, 23, 58, 0, TimeSpan.Zero);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public CompanyBrowserTests() => Directory.CreateDirectory(_root);

    private string PathName => Path.Combine(_root, "company.sqlite");

    private CompanyStore Store(
        string company = "fictional-company",
        string? runtime = Runtime,
        Func<bool>? available = null
    ) =>
        new(
            PathName,
            Owner,
            [new("fictional-role", "Fictional analyst", "Read public pages", ["report"], [])],
            companyId: company
        )
        {
            Clock = () => _now,
            BrowserRuntimeIdentity = runtime,
            BrowserExecutionAvailable = available ?? (() => true),
            SandboxRuntimeIdentity = "fictional-script-v1",
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static T[] Items<T>(CompanyStore store, string key) =>
        JsonSerializer
            .SerializeToElement(store.Dashboard(), CompanyJson.Options)
            .GetProperty(key)
            .Deserialize<T[]>(CompanyJson.Options)!;

    private static CompanyTask TaskFor(CompanyStore store)
    {
        var department = Apply<CompanyDepartment>(store, C("department.create") with { Name = "Fictional research" });
        var employee = Apply<CompanyEmployee>(
            store,
            C("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "fictional-role",
                Name = "Analyst",
                Responsibility = "Review fictional pages",
            }
        );
        var knowledge = Apply<CompanyKnowledge>(
            store,
            C("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = "{}",
            }
        );
        store.Apply(
            C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "approve",
            }
        );
        var thread = Items<CompanyThread>(store, "threads").Single(t => t.EmployeeId == employee.Id);
        return Apply<CompanyTask>(
            store,
            C("task.create") with
            {
                EmployeeId = employee.Id,
                ThreadId = thread.Id,
                Skill = "report",
                Body = "Review the fictional catalog",
            }
        );
    }

    private static void Configure(CompanyStore store, int jobs = 20, int seconds = 1800, bool paused = false) =>
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = store.ComputeUsage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyComputeLimits(jobs, seconds, paused)),
            }
        );

    private static CompanyBrowserJob Propose(CompanyStore store, CompanyTask task) =>
        Apply<CompanyBrowserJob>(
            store,
            C("browser.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = CompanyJson.Encode(
                    new CompanyBrowserInput(
                        "Read catalog",
                        ["https://catalog.example"],
                        [new("navigate", "https://catalog.example/items"), new("read", "main")],
                        new(5),
                        Screenshots: false
                    )
                ),
            }
        );

    private static CompanyCommand Start(CompanyBrowserJob job) =>
        C("browser.run") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.Digest,
            Decision = "approve-once",
        };

    private static CompanyCommand Review(CompanyBrowserJob job, string quote = "Fictional catalog: paper") =>
        C("browser.output.prepare") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.ResultDigest,
            Decision = "prepare-review",
            Body = CompanyJson.Encode(new CompanyBrowserArtifactInput(2, quote)),
        };

    private static BrowserExecution Success(BrowserRequest r) =>
        new(
            "completed",
            new(
                r.JobId,
                "completed",
                2,
                false,
                [
                    new(
                        2,
                        "https://catalog.example/items",
                        DateTimeOffset.UtcNow,
                        "Catalog",
                        "Fictional catalog: paper",
                        false,
                        []
                    ),
                ],
                [],
                [],
                true,
                new(1, 50),
                10
            ),
            new(2, 70, 0),
            30,
            new(false, "awaiting-release", "fictional-" + r.JobId),
            CreationConfirmed: true,
            BrowserExited: true
        );

    private sealed class Executor(CompanyStore store) : IBrowserExecutor
    {
        public string Identity { get; set; } = Runtime;
        public int Executions,
            Recoveries,
            Releases;
        public bool ReleaseConfirmed = true;
        public Func<BrowserRequest, CancellationToken, Task<BrowserExecution>>? Execute;
        public BrowserExecution? Recovered;

        public Task<BrowserExecution> ExecuteAsync(
            BrowserRequest request,
            Func<bool> stillAuthorized,
            CancellationToken token
        )
        {
            Executions++;
            Assert.True(stillAuthorized());
            Assert.True(store.BrowserJob(request.JobId).DispatchTaken);
            return Execute?.Invoke(request, token) ?? Task.FromResult(Success(request));
        }

        public Task<BrowserExecution> RecoverAsync(BrowserRequest request, CancellationToken token)
        {
            Recoveries++;
            return Task.FromResult(Recovered ?? Success(request) with { Network = null, WallMilliseconds = null });
        }

        public Task<SandboxRelease> ReleaseAsync(
            BrowserRequest request,
            string resourceId,
            bool creationConfirmed,
            CancellationToken token
        )
        {
            Releases++;
            var saved = store.BrowserJob(request.JobId);
            Assert.NotNull(saved.Execution);
            Assert.Equal(saved.Execution.Release.ResourceId, resourceId);
            Assert.Equal(saved.Execution.CreationConfirmed, creationConfirmed);
            Assert.Equal(store.CompanyId, request.CompanyId);
            Assert.Equal(CompanyJson.Encode(saved.Request), CompanyJson.Encode(request));
            return Task.FromResult(
                new SandboxRelease(ReleaseConfirmed, ReleaseConfirmed ? "released" : "release-unconfirmed", resourceId)
            );
        }
    }

    [Fact]
    public async Task ExactApprovalDispatchPersistenceAndReviewProduceOneCompletedTask()
    {
        var store = Store();
        Configure(store);
        var task = TaskFor(store);
        var proposal = Propose(store, task);
        var command = Start(proposal);
        Assert.Throws<WorkspaceException>(() => store.Apply(command with { Digest = "different" }));
        store.Apply(command);
        Assert.Equal(17, store.ComputeUsage().HeldSeconds);
        var executor = new Executor(store);
        var job = await new CompanyBrowserRunner(store, executor).RunAsync(proposal.Id, Token);
        Assert.Equal("completed", job.State);
        Assert.True(job.Release!.Confirmed);
        Assert.Equal(17, job.AllocatedSeconds);
        Assert.Equal(0, store.ComputeUsage().HeldSeconds);
        Assert.Equal(CompanyJson.Encode(job), CompanyJson.Encode(Store().BrowserJob(job.Id)));
        store.Apply(command); // Replay returns its old operation receipt; it cannot enqueue another run.
        await new CompanyBrowserRunner(store, executor).RunAsync(job.Id, Token);
        Assert.Equal(1, executor.Executions);
        Assert.Equal(1, executor.Releases);
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(job)));
        var artifact = Apply<CompanyArtifact>(Store(runtime: null), Review(job));
        Assert.Equal(artifact.Id, Apply<CompanyArtifact>(store, Review(job)).Id);
        Assert.Equal("browser-capture", artifact.Provenance);
        Assert.Equal("https://catalog.example/items", artifact.BrowserOrigin!.Url);
        Assert.Equal(job.ResultDigest, artifact.BrowserOrigin.ResultDigest);
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version })
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
        store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version });
        Assert.Equal("completed", store.Work(task.Id).State);
        Assert.Contains(
            store.Inbox(Owner).Items,
            n => n.Notice.Title == "Website captures are ready for review" && n.Notice.TargetId == task.Id
        );
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public async Task ScriptAndBrowserReservationsShareLimitsAndUnresolvedTimeCrossesMonths()
    {
        var store = Store();
        Configure(store, seconds: 21);
        var task = TaskFor(store);
        var browser = Propose(store, task);
        store.Apply(Start(browser));
        // Independent assignments share one purse; a single assignment cannot employ two runtimes at once.
        var scriptTask = TaskFor(store);
        var script = Apply<CompanySandboxJob>(
            store,
            C("sandbox.prepare") with
            {
                Id = scriptTask.Id,
                ExpectedVersion = scriptTask.Version,
                Body = CompanyJson.Encode(new CompanySandboxInput("Count", "console.log(1)", [], [], new(5))),
            }
        );
        var startScript = C("sandbox.run") with
        {
            Id = script.Id,
            ExpectedVersion = script.Version,
            Digest = script.Digest,
            Decision = "approve-once",
        };
        Assert.Equal(429, Assert.Throws<WorkspaceException>(() => store.Apply(startScript)).Status);
        Configure(store, seconds: 22);
        store.Apply(startScript);
        Assert.Equal(22, store.ComputeUsage().HeldSeconds);
        Assert.Equal(2, store.ComputeUsage().HeldJobs);
        var summaries = store.ComputeSummaries();
        Assert.Equal(17, Assert.Single(summaries, j => j.Kind == "browser").HeldSeconds);
        Assert.Equal(5, Assert.Single(summaries, j => j.Kind == "script").HeldSeconds);
        Assert.NotNull(store.TakeBrowserDispatch(browser.Id, Runtime));
        _now = _now.AddMinutes(3);
        var next = Propose(store, task);
        Assert.Equal(22, store.ComputeUsage().CarriedSeconds);
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(next)));
        var executor = new Executor(store) { ReleaseConfirmed = false };
        var recovered = await new CompanyBrowserRunner(store, executor).RecoverAsync(browser.Id, Token);
        Assert.Null(recovered.SettledAt);
        Assert.Null(recovered.Execution!.Network);
        Assert.Equal(22, store.ComputeUsage().CarriedSeconds);
        executor.ReleaseConfirmed = true;
        await new CompanyBrowserRunner(store, executor).RecoverAsync(browser.Id, Token);
        Assert.Equal(5, store.ComputeUsage().CarriedSeconds);
        store.Apply(Start(next));
        Assert.Equal(17, store.ComputeUsage().HeldSeconds);
        Assert.Equal(0, executor.Executions);
    }

    [Fact]
    public async Task RestartRecoversOriginalOutputWithoutNetworkMeasurementOrAnotherVisit()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        Assert.NotNull(store.TakeBrowserDispatch(job.Id, Runtime));
        Assert.Null(Store().TakeBrowserDispatch(job.Id, Runtime));
        var replacement = new Executor(store) { Identity = "replacement-browser" };
        await new CompanyBrowserRunner(Store(runtime: replacement.Identity), replacement).RecoverAsync(job.Id, Token);
        Assert.Equal(0, replacement.Recoveries + replacement.Releases);
        var restored = Store(available: () => false);
        var executor = new Executor(restored);
        var recovered = await new CompanyBrowserRunner(restored, executor).RecoverAsync(job.Id, Token);
        Assert.Equal("completed", recovered.State);
        Assert.True(recovered.Release!.Confirmed);
        Assert.Null(recovered.Execution!.Network);
        Assert.Null(recovered.Execution.WallMilliseconds);
        Assert.Equal(1, recovered.Execution.Result!.Network.Attempts);
        Assert.Equal(0, executor.Executions);
        Assert.Equal(1, executor.Recoveries);
    }

    [Fact]
    public async Task PausedOrUnavailableQueuedWorkSettlesAsUnsentWithoutTouchingExecutor()
    {
        var ready = true;
        var store = Store(available: () => ready);
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        ready = false;
        var executor = new Executor(store);
        var settled = await new CompanyBrowserRunner(store, executor).RunAsync(job.Id, Token);
        Assert.Equal("not-sent", settled.State);
        Assert.Equal(0, settled.AllocatedSeconds);
        Assert.True(settled.Release!.Confirmed);
        Assert.Equal(0, executor.Executions + executor.Recoveries + executor.Releases);
        Assert.Null(store.TakeBrowserDispatch(job.Id, Runtime));
    }

    [Fact]
    public async Task RevocationWinsWorkerProgressRaceAndWithholdsLateCaptures()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var canceled = false;
        var executor = new Executor(store)
        {
            Execute = async (request, token) =>
            {
                store.Apply(C("browser.revoke") with { Id = job.Id, ExpectedVersion = job.Version });
                try
                {
                    await Task.Delay(3000, token);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }
                return Success(request);
            },
        };
        var result = await new CompanyBrowserRunner(store, executor).RunAsync(job.Id, Token);
        Assert.True(canceled);
        Assert.Equal("withheld", result.State);
        Assert.Null(result.Execution!.Result);
        Assert.True(result.Release!.Confirmed);
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(result)));
    }

    [Fact]
    public async Task OnlyActiveOwnerCanApproveAndOwnerRevocationFencesRecovery()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = CompanyRoles.Approver });
        store.SignIn("reviewer", "reviewer@example.invalid", true, "Reviewer");
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(job), "reviewer", out _));
        store.Apply(Start(job));
        Assert.NotNull(store.TakeBrowserDispatch(job.Id, Runtime));
        store.Apply(C("member.invite") with { Body = "next@example.invalid", Decision = CompanyRoles.Owner });
        store.SignIn("next", "next@example.invalid", true, "Next owner");
        var owner = store.Members().Single(m => m.Id == Owner);
        store.Apply(C("member.revoke") with { Id = owner.Id, ExpectedVersion = owner.Version }, "next", out _);
        var result = await new CompanyBrowserRunner(store, new Executor(store)).RecoverAsync(job.Id, Token);
        Assert.Equal("withheld", result.State);
        Assert.Null(result.Execution!.Result);
        Assert.True(result.Release!.Confirmed);
    }

    [Fact]
    public void PauseExpiryContextChangesAndForeignCompanyAllRefuseAuthority()
    {
        var store = Store();
        var task = TaskFor(store);
        var job = Propose(store, task);
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(job)));
        Configure(store);
        _now = _now.AddHours(2);
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(job)));
        var fresh = Propose(store, task);
        Assert.Throws<WorkspaceException>(() => Store("foreign-company").Apply(Start(fresh)));
        Assert.Throws<WorkspaceException>(() => Store("foreign-company").BrowserJob(fresh.Id));
        var employee = Items<CompanyEmployee>(store, "employees").Single(e => e.Id == task.EmployeeId);
        store.Apply(
            C("employee.lifecycle") with
            {
                Id = employee.Id,
                ExpectedVersion = employee.Version,
                Decision = "pause",
            }
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(fresh)));
        Assert.Empty(store.PendingBrowserRecovery());
    }

    [Fact]
    public async Task MalformedOrForeignReceiptCannotBeSavedOrTriggerLogRemoval()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var executor = new Executor(store)
        {
            Execute = (r, _) =>
                Task.FromResult(
                    Success(r) with
                    {
                        Result = Success(r).Result! with { JobId = Guid.NewGuid().ToString("N") },
                    }
                ),
        };
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            new CompanyBrowserRunner(store, executor).RunAsync(job.Id, Token)
        );
        Assert.Null(store.BrowserJob(job.Id).Execution);
        Assert.Equal(0, executor.Releases);
        Assert.Equal(17, store.ComputeUsage().HeldSeconds);
        Assert.Throws<WorkspaceException>(() =>
            store.RecordBrowserRelease(job.Id, Runtime, new(true, "released", "foreign"))
        );
        var good = Success(job.Request);
        Assert.Throws<WorkspaceException>(() =>
            store.RecordBrowserResult(job.Id, Runtime, good with { Network = new(101, 0, 0) })
        );
        Assert.Throws<WorkspaceException>(() =>
            store.RecordBrowserResult(job.Id, Runtime, good with { State = "not-sent" })
        );
        store.RecordBrowserResult(job.Id, Runtime, good);
        Assert.Throws<WorkspaceException>(() =>
            store.RecordBrowserRelease(job.Id, Runtime, new(true, "released", "foreign"))
        );
        Assert.Null(store.BrowserJob(job.Id).SettledAt);
    }

    [Fact]
    public async Task LostAcknowledgementRecoversWithoutReplayingAndUnknownCleanupKeepsHold()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var executor = new Executor(store)
        {
            Execute = (_, _) => throw new IOException("fictional lost acknowledgement"),
            ReleaseConfirmed = false,
            Recovered = new(
                "unknown",
                null,
                null,
                null,
                new(false, "awaiting-reconciliation", "fictional-" + job.Id),
                "execution-unconfirmed",
                true
            ),
        };
        var result = await new CompanyBrowserRunner(store, executor).RunAsync(job.Id, Token);
        Assert.Equal("unknown", result.State);
        Assert.Null(result.SettledAt);
        Assert.Equal(1, executor.Executions);
        Assert.Equal(1, executor.Recoveries);
        Assert.Equal(17, store.ComputeUsage().HeldSeconds);
        executor.Recovered = Success(job.Request) with { Network = null, WallMilliseconds = null };
        executor.ReleaseConfirmed = true;
        result = await new CompanyBrowserRunner(Store(), executor).RecoverAsync(job.Id, Token);
        Assert.Equal("completed", result.State);
        Assert.True(result.Release!.Confirmed);
        Assert.Equal(1, executor.Executions);
    }

    [Fact]
    public async Task RevokedBrowserEvidenceCannotCompleteWorkOrFeedAnotherTask()
    {
        var store = Store();
        Configure(store);
        var task = TaskFor(store);
        var job = Propose(store, task);
        store.Apply(Start(job));
        job = await new CompanyBrowserRunner(store, new Executor(store)).RunAsync(job.Id, Token);
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(job, "Invented content")));
        var artifact = Apply<CompanyArtifact>(store, Review(job));
        artifact = Apply<CompanyArtifact>(
            store,
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
        var recipient = TaskFor(store);
        var dependent = Apply<CompanyTask>(
            store,
            C("task.create") with
            {
                EmployeeId = recipient.EmployeeId,
                ThreadId = recipient.ThreadId,
                Skill = "report",
                Body = "Use approved catalog evidence",
                Evidence = [artifact.Id],
            }
        );
        var attempt = store.Claim(recipient.EmployeeId, true, dependent.Id)!;
        Assert.Equal(artifact.Id, Assert.Single(store.Capture(attempt.Id).Evidence).Id);
        store.Apply(C("browser.revoke") with { Id = job.Id, ExpectedVersion = 1 });
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version })
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(job)));
        Assert.Throws<WorkspaceException>(() => store.Capture(attempt.Id));
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("handoff.assign") with
                {
                    Id = artifact.Id,
                    ExpectedVersion = artifact.Version,
                    Digest = artifact.Digest,
                    EmployeeId = recipient.EmployeeId,
                    ThreadId = recipient.ThreadId,
                    Skill = "report",
                    Body = "Use the catalog",
                }
            )
        );
    }

    [Fact]
    public async Task PartialCaptureKeepsItsProvenanceAndUnconfirmedBrowserExitBlocksReview()
    {
        var store = Store();
        Configure(store);
        var task = TaskFor(store);
        var job = Propose(store, task);
        store.Apply(Start(job));
        var executor = new Executor(store)
        {
            Execute = (r, _) =>
                Task.FromResult(
                    Success(r) with
                    {
                        Result = Success(r).Result! with { Partial = true, Warnings = ["Fictional blocked resource"] },
                    }
                ),
        };
        job = await new CompanyBrowserRunner(store, executor).RunAsync(job.Id, Token);
        Assert.True(Apply<CompanyArtifact>(store, Review(job)).BrowserOrigin!.Partial);
        var next = Propose(store, store.Work(task.Id));
        store.Apply(Start(next));
        executor.Execute = (r, _) => Task.FromResult(Success(r) with { BrowserExited = false });
        next = await new CompanyBrowserRunner(store, executor).RunAsync(next.Id, Token);
        Assert.Equal("cleanup-incomplete", next.State);
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(next)));
    }

    [Fact]
    public void Migration23PreservesEarlierRecordsAndChecksums()
    {
        var store = Store();
        var task = TaskFor(store);
        using (var db = new SqliteConnection("Data Source=" + PathName))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText =
                "DROP TABLE IF EXISTS co_member_invites; DROP TABLE IF EXISTS co_decisions; DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE co_browser_retention; DROP TABLE co_browser_jobs; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=23;";
            command.ExecuteNonQuery();
        }
        var upgraded = Store();
        Assert.Equal(CompanyJson.Encode(task), CompanyJson.Encode(upgraded.Work(task.Id)));
        Assert.Empty(upgraded.PendingBrowserRecovery());
        Assert.Equal("proposed", Propose(upgraded, task).State);
    }

    [Fact]
    public async Task CompletingTheParentTaskStopsPendingAndActiveBrowserAuthority()
    {
        var store = Store();
        Configure(store);
        var task = TaskFor(store);
        var first = Propose(store, task);
        store.Apply(Start(first));
        first = await new CompanyBrowserRunner(store, new Executor(store)).RunAsync(first.Id, Token);
        var artifact = Apply<CompanyArtifact>(store, Review(first));
        store.Apply(
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
        var pending = Propose(store, store.Work(task.Id));
        var active = Propose(store, store.Work(task.Id));
        store.Apply(Start(active));
        Assert.NotNull(store.TakeBrowserDispatch(active.Id, Runtime));
        Assert.True(store.BrowserAuthorized(active.Id));
        store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version });
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(pending)));
        Assert.False(store.BrowserAuthorized(active.Id));
        var late = store.RecordBrowserResult(active.Id, Runtime, Success(active.Request));
        Assert.Equal("withheld", late.State);
        Assert.Null(late.Execution!.Result);
        Assert.Equal(17, store.ComputeUsage().HeldSeconds);
        store.RecordBrowserRelease(active.Id, Runtime, new(true, "released", late.Execution.Release.ResourceId));
        Assert.Equal(0, store.ComputeUsage().HeldSeconds);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }
}
