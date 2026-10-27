using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Sandbox;
using Microsoft.Data.Sqlite;

public sealed partial class CompanySandboxTests : IDisposable
{
    private const string Owner = "owner@example.invalid",
        Runtime = "fictional-sandbox-v1";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sandbox-ledger-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 31, 23, 58, 0, TimeSpan.Zero);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public CompanySandboxTests() => Directory.CreateDirectory(_root);

    private CompanyStore Store(
        string company = "fictional-company",
        string? runtime = Runtime,
        bool publishing = false
    ) =>
        new(
            Path.Combine(_root, "company.sqlite"),
            Owner,
            [
                new("fictional-role", "Fictional analyst", "Prepare private reports", ["report"], [])
                {
                    PublishingSkills = publishing ? ["report"] : [],
                },
            ],
            companyId: company
        )
        {
            Clock = () => _now,
            SandboxRuntimeIdentity = runtime,
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
        var department = Apply<CompanyDepartment>(store, C("department.create") with { Name = "Fictional reports" });
        var employee = Apply<CompanyEmployee>(
            store,
            C("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "fictional-role",
                Name = "Analyst",
                Responsibility = "Prepare fictional reports",
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
                Body = "Summarize fictional rows",
            }
        );
    }

    private static CompanyDocument Document(CompanyStore store, string? employee = null)
    {
        var draft = Apply<CompanyDocument>(
            store,
            C("document.save") with
            {
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(
                    new CompanyDocumentInput(
                        "Fictional rows",
                        "label,count\nPaper,3",
                        EmployeeId: employee,
                        SourceName: "rows.txt"
                    )
                ),
            }
        );
        return Apply<CompanyDocument>(
            store,
            C("document.review") with
            {
                Id = draft.Id,
                ExpectedVersion = draft.Version,
                Digest = draft.Digest,
                Decision = "approve",
            }
        );
    }

    private static CompanyComputePolicy Configure(
        CompanyStore store,
        int jobs = 20,
        int seconds = 1800,
        bool paused = false
    ) =>
        Apply<CompanyComputePolicy>(
            store,
            C("compute.configure") with
            {
                ExpectedVersion = store.ComputeUsage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyComputeLimits(jobs, seconds, paused)),
            }
        );

    private static CompanySandboxJob Propose(
        CompanyStore store,
        CompanyTask task,
        params CompanyDocument[] documents
    ) =>
        Apply<CompanySandboxJob>(
            store,
            C("sandbox.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = CompanyJson.Encode(
                    new CompanySandboxInput(
                        "Summarize rows",
                        "console.log('Fictional report')",
                        documents.Select((d, i) => new CompanySandboxSelection(d.Id, "input" + i + ".txt")).ToArray(),
                        ["report.txt"],
                        new(5)
                    )
                ),
            }
        );

    private static CompanyCommand Start(CompanySandboxJob job) =>
        C("sandbox.run") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.Digest,
            Decision = "approve-once",
        };

    private static SandboxResult Success(CompanySandboxJob job) =>
        new(
            "completed",
            0,
            "Fictional report",
            "",
            [new("report.txt", "Paper: 3")],
            25,
            new(false, "awaiting-release", "fictional-" + job.Id),
            CreationConfirmed: true
        );

    private sealed class Executor(CompanyStore store) : ISandboxExecutor
    {
        public string Identity { get; set; } = Runtime;
        public int Executions,
            Recoveries,
            Releases;
        public bool ReleaseConfirmed = true;
        public Func<SandboxRequest, CancellationToken, Task<SandboxResult>>? Execute;
        public SandboxResult? Recovered;
        public Action? OnRelease;

        public Task<SandboxResult> ExecuteAsync(
            SandboxRequest request,
            Func<bool> stillAuthorized,
            CancellationToken token
        )
        {
            Executions++;
            Assert.True(stillAuthorized());
            Assert.True(store.SandboxJob(request.JobId).DispatchTaken);
            return Execute?.Invoke(request, token) ?? Task.FromResult(Success(store.SandboxJob(request.JobId)));
        }

        public Task<SandboxResult> RecoverAsync(SandboxRequest request, CancellationToken token)
        {
            Recoveries++;
            return Task.FromResult(
                Recovered ?? Success(store.SandboxJob(request.JobId)) with { WallMilliseconds = null }
            );
        }

        public Task<SandboxRelease> ReleaseAsync(
            string companyId,
            string jobId,
            bool creationConfirmed,
            CancellationToken token
        )
        {
            Releases++;
            Assert.Equal(store.CompanyId, companyId);
            Assert.NotNull(store.SandboxJob(jobId).Result); // Persisted result precedes irreversible log removal.
            OnRelease?.Invoke();
            return Task.FromResult(
                new SandboxRelease(
                    ReleaseConfirmed,
                    ReleaseConfirmed ? "released" : "release-unconfirmed",
                    "fictional-" + jobId
                )
            );
        }
    }

    [Fact]
    public async Task ExactApprovalAndDurableDispatchPreventReplayAcrossInstances()
    {
        var store = Store();
        Configure(store);
        var document = Document(store);
        var job = Propose(store, TaskFor(store), document);
        Assert.Equal(document.Text, Assert.Single(job.Request.Inputs).Text);
        Assert.Equal(0, store.ComputeUsage().Jobs);
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(job) with { Digest = "changed" }));
        var start = Start(job);
        var first = store.Apply(start, out var applied);
        Assert.True(applied);
        Assert.Equal(first, Store().Apply(start, out applied));
        Assert.False(applied);
        var claims = await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(_ => System.Threading.Tasks.Task.Run(() => Store().TakeSandboxDispatch(job.Id, Runtime), Token))
        );
        Assert.Single(claims, j => j is not null);
        var executor = new Executor(Store());
        await new CompanySandboxRunner(Store(), executor).RunAsync(job.Id, Token);
        Assert.Equal(0, executor.Executions);
        Assert.Equal(1, store.ComputeUsage().HeldJobs);
    }

    [Fact]
    public async Task ConcurrentMonthlyAdmissionCannotSpendTheLastAllocationTwice()
    {
        var store = Store();
        Configure(store, 20, 5);
        var task = TaskFor(store);
        var jobs = new[] { Propose(store, task), Propose(store, task) };
        var started = await Task.WhenAll(
            jobs.Select(job =>
                System.Threading.Tasks.Task.Run(
                    () =>
                    {
                        try
                        {
                            Store().Apply(Start(job));
                            return true;
                        }
                        catch (WorkspaceException)
                        {
                            return false;
                        }
                    },
                    Token
                )
            )
        );
        Assert.Single(started, success => success);
        Assert.Equal(5, store.ComputeUsage().HeldSeconds);
        Assert.Equal(0, store.Usage().Current.Jobs);
    }

    [Fact]
    public void ProposalCannotExpandDocumentAudienceOrCrossCompanyBoundary()
    {
        var store = Store();
        var task = TaskFor(store);
        var other = TaskFor(store);
        var privateDocument = Document(store, other.EmployeeId);
        Assert.Throws<WorkspaceException>(() => Propose(store, task, privateDocument));
        var foreign = Store("another-company");
        var foreignDocument = Document(foreign);
        Assert.Throws<WorkspaceException>(() => Propose(store, task, foreignDocument));
        var own = Propose(store, task, Document(store, task.EmployeeId));
        Assert.Throws<WorkspaceException>(() => foreign.SandboxJob(own.Id));
        Assert.Throws<WorkspaceException>(() => foreign.TakeSandboxDispatch(own.Id, Runtime));
        Assert.Empty(foreign.PendingSandboxRecovery());
    }

    [Fact]
    public async Task ResultSurvivesFailedCleanupAndRestartWithoutExecutingAgain()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var executor = new Executor(store)
        {
            ReleaseConfirmed = false,
            OnRelease = () => Assert.Equal("completed", Store().SandboxJob(job.Id).State),
        };
        var finished = await new CompanySandboxRunner(store, executor).RunAsync(job.Id, Token);
        Assert.Equal("completed", finished.State);
        Assert.Null(finished.SettledAt);
        Assert.Equal("Paper: 3", Assert.Single(Store().SandboxJob(job.Id).Result!.Outputs).Text);
        var notices = Store().Inbox(Owner).Items;
        Assert.Contains(
            notices,
            n => n.Notice.Destination == "task" && n.Notice.TargetId == job.TaskId && n.Notice.Category == "completed"
        );
        Assert.Contains(notices, n => n.Notice.TargetId == job.TaskId && n.Notice.Category == "attention");
        await new CompanySandboxRunner(Store(), executor).RecoverAsync(job.Id, Token);
        Assert.Equal(notices.Length, Store().Inbox(Owner).Items.Length);
        var next = new Executor(Store());
        var recovered = await new CompanySandboxRunner(Store(), next).RecoverAsync(job.Id, Token);
        Assert.NotNull(recovered.SettledAt);
        Assert.Equal(0, next.Executions);
        Assert.Equal(0, next.Recoveries);
        Assert.Equal(1, next.Releases);
        Assert.Equal(finished.ResultDigest, recovered.ResultDigest);
        Assert.Equal(5, Store().ComputeUsage().AllocatedSeconds);
        await new CompanySandboxRunner(Store(), next).RecoverAsync(job.Id, Token);
        Assert.Equal(1, next.Releases);
    }

    [Fact]
    public async Task CrashBeforeDispatchReleasesReservationWithoutCallingInfrastructure()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var executor = new Executor(Store());
        var result = await new CompanySandboxRunner(Store(), executor).RecoverAsync(job.Id, Token);
        Assert.Equal("not-sent", result.State);
        Assert.Equal(0, result.AllocatedSeconds);
        Assert.True(result.Release!.Confirmed);
        Assert.Equal(0, executor.Executions + executor.Recoveries + executor.Releases);
        Assert.Null(Store().TakeSandboxDispatch(job.Id, Runtime));
    }

    [Fact]
    public async Task CrashAfterDispatchRecoversOutputAfterDeadlineWithoutInventingMeasuredDuration()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        Assert.NotNull(store.TakeSandboxDispatch(job.Id, Runtime));
        _now = _now.AddDays(2);
        var executor = new Executor(Store());
        var recovered = await new CompanySandboxRunner(Store(), executor).RecoverAsync(job.Id, Token);
        Assert.Equal("completed", recovered.State);
        Assert.Null(recovered.Result!.WallMilliseconds);
        Assert.Equal("Paper: 3", Assert.Single(recovered.Result.Outputs).Text);
        Assert.Equal(0, executor.Executions);
        Assert.Equal(1, executor.Recoveries);
        Assert.Equal(0, Store().ComputeUsage().CarriedJobs);
    }

    [Fact]
    public async Task UncertainProvisioningCarriesAllowanceAcrossMonthUntilReleaseIsConfirmed()
    {
        var store = Store();
        Configure(store, 1, 5);
        var task = TaskFor(store);
        var job = Propose(store, task);
        store.Apply(Start(job));
        Assert.NotNull(store.TakeSandboxDispatch(job.Id, Runtime));
        var unknown = new SandboxResult(
            "unknown",
            null,
            "",
            "",
            [],
            null,
            new(false, "awaiting-reconciliation", "fictional-" + job.Id),
            "recovery-unconfirmed"
        );
        var executor = new Executor(store) { Recovered = unknown, ReleaseConfirmed = false };
        await new CompanySandboxRunner(store, executor).RecoverAsync(job.Id, Token);
        _now = _now.AddDays(2);
        var usage = Store().ComputeUsage();
        Assert.Equal(0, usage.Jobs);
        Assert.Equal(1, usage.CarriedJobs);
        Assert.Equal(5, usage.CarriedSeconds);
        var later = Propose(Store(), task);
        Assert.Throws<WorkspaceException>(() => Store().Apply(Start(later)));
        executor.ReleaseConfirmed = true;
        await new CompanySandboxRunner(Store(), executor).RecoverAsync(job.Id, Token);
        Assert.Equal(0, Store().ComputeUsage().CarriedJobs);
        Store().Apply(Start(later));
        Assert.Equal(1, Store().ComputeUsage().HeldJobs);
        Assert.Equal(0, executor.Executions);
    }

    [Fact]
    public async Task RevokingInputCancelsExecutionAndWithholdsLateOutput()
    {
        var store = Store();
        Configure(store);
        var document = Document(store);
        var job = Propose(store, TaskFor(store), document);
        store.Apply(Start(job));
        var executor = new Executor(store);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        executor.Execute = async (_, token) =>
        {
            entered.SetResult();
            try
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            Assert.True(token.IsCancellationRequested);
            return Success(job);
        };
        var running = new CompanySandboxRunner(store, executor).RunAsync(job.Id, Token);
        await entered.Task.WaitAsync(Token);
        store.Apply(
            C("document.review") with
            {
                Id = document.Id,
                ExpectedVersion = document.Version,
                Digest = document.Digest,
                Decision = "revoke",
            }
        );
        var result = await running;
        Assert.Equal("withheld", result.State);
        Assert.Empty(result.Result!.Outputs);
        Assert.Empty(result.Result.Stdout);
        Assert.True(result.Release!.Confirmed);
        Assert.Equal(5, result.AllocatedSeconds);
    }

    [Fact]
    public async Task RevocationBeforeClaimSettlesUnsentWithoutLeavingARunningCard()
    {
        var store = Store();
        Configure(store);
        var job = Apply<CompanySandboxJob>(store, Start(Propose(store, TaskFor(store))));
        store.Apply(C("sandbox.revoke") with { Id = job.Id, ExpectedVersion = job.Version });
        var executor = new Executor(store);
        var result = await new CompanySandboxRunner(store, executor).RunAsync(job.Id, Token);
        Assert.Equal("not-sent", result.State);
        Assert.Equal(0, result.AllocatedSeconds);
        Assert.Equal(0, executor.Executions);
    }

    [Fact]
    public async Task MalformedOutputCannotTriggerReleaseOrBecomeDurableContent()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var executor = new Executor(store)
        {
            Execute = (_, _) =>
                System.Threading.Tasks.Task.FromResult(
                    Success(job) with
                    {
                        Outputs = [new("unexpected.txt", "Not authorized")],
                    }
                ),
        };
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            new CompanySandboxRunner(store, executor).RunAsync(job.Id, Token)
        );
        Assert.Null(Store().SandboxJob(job.Id).Result);
        Assert.Equal(0, executor.Releases);
        Assert.Equal(1, Store().ComputeUsage().HeldJobs);
    }

    [Fact]
    public async Task ChangedRuntimeCannotDispatchOrReleaseAnOlderApproval()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var changed = Store(runtime: "fictional-sandbox-v2");
        var executor = new Executor(changed) { Identity = "fictional-sandbox-v2" };
        await new CompanySandboxRunner(changed, executor).RunAsync(job.Id, Token);
        await new CompanySandboxRunner(changed, executor).RecoverAsync(job.Id, Token);
        Assert.Equal(0, executor.Executions + executor.Recoveries + executor.Releases);
        Assert.Equal(1, changed.ComputeUsage().HeldJobs);
    }

    [Fact]
    public void DefaultPauseExpiredApprovalAndChangedEmployeeAllRefuseAdmission()
    {
        var store = Store();
        var task = TaskFor(store);
        var job = Propose(store, task);
        Assert.True(store.ComputeUsage().Policy.Limits.Paused);
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(job)));
        Configure(store);
        _now = _now.AddHours(2);
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(job)));
        var current = Propose(store, task);
        var employee = Items<CompanyEmployee>(store, "employees").Single();
        store.Apply(
            C("employee.update") with
            {
                Id = employee.Id,
                ExpectedVersion = employee.Version,
                Responsibility = "New fictional responsibility",
            }
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(current)));
        Assert.Equal(0, store.ComputeUsage().Jobs);
        Assert.Equal(CompanyRoles.Owner, CompanyAccess.Required("sandbox.run"));
        Assert.Equal(CompanyRoles.Owner, CompanyAccess.Required("compute.configure"));
    }

    [Fact]
    public async Task ExecutorExceptionRecoversWithoutRepeatingProvisioning()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var executor = new Executor(store)
        {
            Execute = (_, _) => throw new IOException("Fictional response lost after execution"),
        };
        var result = await new CompanySandboxRunner(store, executor).RunAsync(job.Id, Token);
        Assert.Equal("completed", result.State);
        Assert.True(result.Release!.Confirmed);
        Assert.Equal(1, executor.Executions);
        Assert.Equal(1, executor.Recoveries);
        Assert.Equal(1, executor.Releases);
    }

    [Fact]
    public async Task PausingComputeCancelsAuthorityAndUnknownCleanupNeverReleasesTheHold()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        Assert.NotNull(store.TakeSandboxDispatch(job.Id, Runtime));
        Assert.True(store.SandboxAuthorized(job.Id));
        Configure(store, paused: true);
        Assert.False(store.SandboxAuthorized(job.Id));
        var executor = new Executor(store) { ReleaseConfirmed = false };
        var result = await new CompanySandboxRunner(store, executor).RecoverAsync(job.Id, Token);
        Assert.Equal("withheld", result.State);
        Assert.Empty(result.Result!.Outputs);
        Assert.Null(result.SettledAt);
        Assert.Equal(5, store.ComputeUsage().HeldSeconds);
    }

    [Fact]
    public async Task OnlyAnActiveOwnerCanAuthorizeAndRemovingThatOwnerWithholdsLateResults()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = CompanyRoles.Approver });
        Assert.NotNull(store.SignIn("fictional-reviewer", "reviewer@example.invalid", true, "Reviewer"));
        Assert.Throws<WorkspaceException>(() => store.Apply(Start(job), "fictional-reviewer", out _));
        store.Apply(Start(job));
        Assert.NotNull(store.TakeSandboxDispatch(job.Id, Runtime));
        store.Apply(C("member.invite") with { Body = "next-owner@example.invalid", Decision = CompanyRoles.Owner });
        Assert.NotNull(store.SignIn("fictional-next-owner", "next-owner@example.invalid", true, "Next owner"));
        var owner = store.Members().Single(m => m.Id == Owner);
        store.Apply(
            C("member.revoke") with
            {
                Id = owner.Id,
                ExpectedVersion = owner.Version,
            },
            "fictional-next-owner",
            out _
        );
        Assert.False(store.SandboxAuthorized(job.Id));
        var result = await new CompanySandboxRunner(store, new Executor(store)).RecoverAsync(job.Id, Token);
        Assert.Equal("withheld", result.State);
        Assert.Empty(result.Result!.Outputs);
        Assert.True(result.Release!.Confirmed);
    }

    [Fact]
    public void StopAuthorityAcceptsAnOlderReceiptAfterTheWorkerClaimsTheImmutableJob()
    {
        var store = Store();
        Configure(store);
        var proposal = Propose(store, TaskFor(store));
        store.Apply(Start(proposal));
        Assert.NotNull(store.TakeSandboxDispatch(proposal.Id, Runtime));
        Assert.True(store.SandboxAuthorized(proposal.Id));
        store.Apply(C("sandbox.revoke") with { Id = proposal.Id, ExpectedVersion = proposal.Version });
        Assert.False(store.SandboxAuthorized(proposal.Id));
        Assert.True(store.SandboxJob(proposal.Id).Revoked);
    }

    [Fact]
    public void WaitingInTheQueueDoesNotConsumeTheExecutionDeadline()
    {
        var store = Store();
        Configure(store);
        var proposal = Propose(store, TaskFor(store));
        store.Apply(Start(proposal));
        _now = _now.AddMinutes(10);
        Assert.Equal(proposal.ExpiresAt, store.SandboxJob(proposal.Id).ExpiresAt);
        var claimed = store.TakeSandboxDispatch(proposal.Id, Runtime);
        Assert.NotNull(claimed);
        Assert.Equal(_now.AddSeconds(185), claimed.ExpiresAt);
        Assert.True(store.SandboxAuthorized(proposal.Id));
        _now = _now.AddSeconds(186);
        Assert.False(store.SandboxAuthorized(proposal.Id));
        Assert.Null(store.SandboxJob(proposal.Id).SettledAt);
    }

    [Fact]
    public void Migration21PreservesEarlierChecksumsAndCompanyRecords()
    {
        var store = Store();
        var task = TaskFor(store);
        var path = Path.Combine(_root, "company.sqlite");
        using (var db = new SqliteConnection("Data Source=" + path))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText =
                "DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE IF EXISTS co_document_retention; DROP TABLE co_sandbox_jobs; DROP TABLE co_compute_policy; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=21;";
            command.ExecuteNonQuery();
        }
        var upgraded = Store();
        Assert.Equal(CompanyJson.Encode(task), CompanyJson.Encode(Items<CompanyTask>(upgraded, "tasks").Single()));
        Assert.True(upgraded.ComputeUsage().Policy.Limits.Paused);
        Assert.Empty(upgraded.PendingSandboxRecovery());
        Assert.Equal("proposed", Propose(upgraded, task).State);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }
}
