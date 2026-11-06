using HireZero.Core;
using HireZero.Integrations.Sandbox;

public sealed partial class CompanyBrowserTests
{
    private static CompanyCommand DeleteCaptures(CompanyBrowserJob job) =>
        C("browser.captures.delete") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.ResultDigest,
            Decision = "delete-captures",
        };

    private static CompanyCommand Retention(CompanyStore store, int days) =>
        C("browser.retention") with
        {
            ExpectedVersion = store.BrowserRetention().Version,
            Body = CompanyJson.Encode(new CompanyBrowserRetentionInput(days)),
            Decision = "apply-capture-retention",
        };

    [Fact]
    public async Task DeletingRawCapturesPreservesOnlyExistingExactExcerptsAndTheirRevocationFence()
    {
        var store = Store();
        Configure(store);
        var task = TaskFor(store);
        var job = Propose(store, task);
        store.Apply(Start(job));
        job = await new CompanyBrowserRunner(store, new Executor(store)).RunAsync(job.Id, Token);
        var artifact = Apply<CompanyArtifact>(store, Review(job));
        var deleted = Apply<CompanyBrowserJob>(Store(runtime: null), DeleteCaptures(job));
        Assert.Null(deleted.Execution!.Result);
        Assert.Equal(job.ResultDigest, deleted.ResultDigest);
        Assert.Equal(job.AllocatedSeconds, deleted.AllocatedSeconds);
        Assert.Equal(artifact.Id, Assert.Single(deleted.CaptureDeletion!.Artifacts).Id);
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(deleted)));
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
        var recipient = TaskFor(store);
        var dependent = Apply<CompanyTask>(
            store,
            C("task.create") with
            {
                EmployeeId = recipient.EmployeeId,
                ThreadId = recipient.ThreadId,
                Skill = "report",
                Body = "Use the retained approved excerpt",
                Evidence = [artifact.Id],
            }
        );
        var attempt = store.Claim(recipient.EmployeeId, true, dependent.Id)!;
        Assert.Equal(artifact.Id, Assert.Single(store.Capture(attempt.Id).Evidence).Id);
        // A forged source cannot borrow the proof of the original excerpt, even with the same body digest.
        using (var db = new StoreConnection(store.Database.Open(), store.Database))
        {
            var approved = Items<CompanyArtifact>(store, "artifacts").Single(a => a.Id == artifact.Id);
            var forged = approved with
            {
                BrowserOrigin = approved.BrowserOrigin! with { Url = "https://catalog.example/forged" },
            };
            db.Execute(
                "UPDATE co_artifacts SET payload=$2 WHERE company_id=$0 AND id=$1",
                store.CompanyId,
                artifact.Id,
                CompanyJson.Encode(forged)
            );
            Assert.Throws<WorkspaceException>(() => store.Capture(attempt.Id));
            db.Execute(
                "UPDATE co_artifacts SET payload=$2 WHERE company_id=$0 AND id=$1",
                store.CompanyId,
                artifact.Id,
                CompanyJson.Encode(approved)
            );
        }
        store.Apply(C("browser.revoke") with { Id = job.Id, ExpectedVersion = deleted.Version });
        Assert.Throws<WorkspaceException>(() => store.Capture(attempt.Id));
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public async Task CaptureErasureScrubsOldOperationResultsWithoutReplayOrCrossCompanyDeletion()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        var start = Start(job);
        store.Apply(start);
        var executor = new Executor(store);
        job = await new CompanyBrowserRunner(store, executor).RunAsync(job.Id, Token);
        var original = job.Execution!;
        var revoke = C("browser.revoke") with { Id = job.Id, ExpectedVersion = job.Version };
        job = Apply<CompanyBrowserJob>(store, revoke);
        Assert.Contains("Fictional catalog: paper", store.Operation(revoke.RequestId));
        var foreign = Store("fictional-other");
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => foreign.Apply(DeleteCaptures(job))).Status);
        var delete = DeleteCaptures(job);
        var erased = Apply<CompanyBrowserJob>(store, delete);
        foreach (var previous in new[] { start, revoke, delete })
        {
            var replay = store.Apply(previous, out var applied);
            Assert.False(applied);
            Assert.DoesNotContain("Fictional catalog: paper", replay);
            Assert.NotNull(CompanyJson.Decode<CompanyBrowserJob>(replay).CaptureDeletion);
        }
        Assert.Throws<WorkspaceException>(() => store.Apply(delete with { Digest = "changed" }));
        Assert.Null(store.RecordBrowserResult(job.Id, Runtime, original).Execution!.Result);
        await new CompanyBrowserRunner(Store(), executor).RecoverAsync(job.Id, Token);
        Assert.Equal(1, executor.Executions);
        Assert.Null(Store().BrowserJob(job.Id).Execution!.Result);
        using var db = new StoreConnection(store.Database.Open(), store.Database);
        foreach (var table in new[] { "co_browser_jobs", "co_events" })
            Assert.DoesNotContain(
                "Fictional catalog: paper",
                string.Join(
                    "\n",
                    db.Rows($"SELECT payload FROM {table} WHERE company_id=$0", r => r.GetString(0), store.CompanyId)
                )
            );
        Assert.DoesNotContain(
            "Fictional catalog: paper",
            string.Join(
                "\n",
                db.Rows("SELECT result FROM co_operations WHERE company_id=$0", r => r.GetString(0), store.CompanyId)
            )
        );
        Assert.DoesNotContain("Fictional catalog: paper", CompanyJson.Encode(store.AuditEntries()));
        Assert.Empty(erased.CaptureDeletion!.Artifacts);
        Assert.Single(store.AuditEntries(), e => e.Kind == "browser.captures-deleted");
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public async Task CaptureDeletionRequiresOwnerExactReviewAndConfirmedResourceRelease()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var executor = new Executor(store) { ReleaseConfirmed = false };
        job = await new CompanyBrowserRunner(store, executor).RunAsync(job.Id, Token);
        Assert.Throws<WorkspaceException>(() => store.Apply(DeleteCaptures(job)));
        executor.ReleaseConfirmed = true;
        job = await new CompanyBrowserRunner(store, executor).RecoverAsync(job.Id, Token);
        foreach (
            var invalid in new[]
            {
                DeleteCaptures(job) with
                {
                    Digest = "wrong",
                },
                DeleteCaptures(job) with
                {
                    Decision = "",
                },
                DeleteCaptures(job) with
                {
                    ExpectedVersion = job.Version - 1,
                },
            }
        )
            Assert.Throws<WorkspaceException>(() => store.Apply(invalid));
        store.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = CompanyRoles.Approver });
        store.SignIn("reviewer", "reviewer@example.invalid", true, "Reviewer");
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => store.Apply(DeleteCaptures(job), "reviewer", out _)).Status
        );
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => store.Apply(Retention(store, 7), "reviewer", out _)).Status
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(Retention(store, 7) with { Decision = "" }));
        Assert.Throws<WorkspaceException>(() => store.Apply(Retention(store, 8)));
        Assert.NotNull(store.BrowserJob(job.Id).Execution!.Result);
    }

    [Fact]
    public async Task CaptureRetentionIsOptInMeasuredFromCleanupAndKeepsUnresolvedJobs()
    {
        var store = Store();
        Configure(store);
        var task = TaskFor(store);
        var ready = Propose(store, task);
        store.Apply(Start(ready));
        ready = await new CompanyBrowserRunner(store, new Executor(store)).RunAsync(ready.Id, Token);
        var unresolved = Propose(store, task);
        store.Apply(Start(unresolved));
        unresolved = await new CompanyBrowserRunner(store, new Executor(store) { ReleaseConfirmed = false }).RunAsync(
            unresolved.Id,
            Token
        );
        _now = _now.AddDays(30);
        Assert.Equal(0, store.MaintainBrowserRetention());
        var policy = Apply<CompanyBrowserRetention>(store, Retention(store, 30));
        Assert.NotNull(store.BrowserJob(ready.Id).CaptureDeletion);
        Assert.Null(store.BrowserJob(unresolved.Id).CaptureDeletion);
        unresolved = await new CompanyBrowserRunner(store, new Executor(store)).RecoverAsync(unresolved.Id, Token);
        Assert.Equal(0, store.MaintainBrowserRetention());
        _now = _now.AddDays(29);
        Assert.Equal(0, store.MaintainBrowserRetention());
        _now = _now.AddDays(1);
        Assert.Equal(1, Store(runtime: null).MaintainBrowserRetention());
        Assert.Equal(0, store.MaintainBrowserRetention());
        Assert.Equal(policy, Store().BrowserRetention());
        Assert.Equal(0, Store("fictional-other").BrowserRetention().CaptureDays);
        Apply<CompanyBrowserRetention>(store, Retention(store, 0));
        Assert.NotNull(store.BrowserJob(ready.Id).CaptureDeletion);
    }

    [Fact]
    public async Task ExcerptPreparationRacingCaptureDeletionNeverCreatesUnverifiableEvidence()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        job = await new CompanyBrowserRunner(store, new Executor(store)).RunAsync(job.Id, Token);
        CompanyArtifact? artifact = null;
        await Task.WhenAll(
            Task.Run(
                () =>
                {
                    try
                    {
                        artifact = Apply<CompanyArtifact>(Store(), Review(job));
                    }
                    catch (WorkspaceException) { }
                },
                Token
            ),
            Task.Run(() => Apply<CompanyBrowserJob>(Store(), DeleteCaptures(job)), Token)
        );
        var deleted = store.BrowserJob(job.Id);
        Assert.NotNull(deleted.CaptureDeletion);
        if (artifact is not null)
        {
            Assert.Equal(artifact.Id, Assert.Single(deleted.CaptureDeletion.Artifacts).Id);
            store.Apply(
                C("artifact.review") with
                {
                    Id = artifact.Id,
                    ExpectedVersion = artifact.Version,
                    Digest = artifact.Digest,
                    Decision = "approve",
                }
            );
        }
        else
            Assert.Empty(deleted.CaptureDeletion.Artifacts);
    }

    [Fact]
    public async Task RetentionUsesBoundedBatchesAndDisablingItPreservesTheRemainingCaptures()
    {
        var store = Store();
        Configure(store, jobs: 30);
        var task = TaskFor(store);
        var ids = new List<string>();
        for (var i = 0; i < 25; i++)
        {
            var job = Propose(store, task);
            store.Apply(Start(job));
            await new CompanyBrowserRunner(store, new Executor(store)).RunAsync(job.Id, Token);
            ids.Add(job.Id);
        }
        _now = _now.AddDays(8);
        Apply<CompanyBrowserRetention>(store, Retention(store, 7));
        Assert.Equal(20, ids.Count(id => store.BrowserJob(id).CaptureDeletion is not null));
        Apply<CompanyBrowserRetention>(store, Retention(store, 0));
        Assert.Equal(0, store.MaintainBrowserRetention());
        Assert.Equal(5, ids.Count(id => store.BrowserJob(id).Execution?.Result is not null));
        Apply<CompanyBrowserRetention>(store, Retention(store, 7));
        Assert.All(ids, id => Assert.NotNull(store.BrowserJob(id).CaptureDeletion));
        Assert.Equal(0, store.MaintainBrowserRetention());
    }

    [Fact]
    public async Task BrowserRetentionMigrationPreservesJobsAndEarlierChecksumsWithExpiryOff()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        job = await new CompanyBrowserRunner(store, new Executor(store)).RunAsync(job.Id, Token);
        string[] checksums;
        using (var db = new StoreConnection(store.Database.Open(), store.Database))
        {
            checksums = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' AND version<24 ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_member_invites; DROP TABLE IF EXISTS co_decisions; DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE co_browser_retention; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=24;"
            );
        }
        var reopened = Store();
        Assert.Equal(CompanyJson.Encode(job), CompanyJson.Encode(reopened.BrowserJob(job.Id)));
        Assert.Equal(0, reopened.BrowserRetention().CaptureDays);
        Assert.Equal(0, reopened.MaintainBrowserRetention());
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal(
            checksums,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' AND version<24 ORDER BY version",
                r => r.GetString(0)
            )
        );
    }
}
