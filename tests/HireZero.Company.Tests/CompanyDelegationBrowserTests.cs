using HireZero.Core;
using HireZero.Integrations.Sandbox;

public sealed partial class CompanyDelegationTests
{
    private static CompanyBrowserJob PrepareWebsite(CompanyStore store, string taskId) =>
        A<CompanyBrowserJob>(
            store,
            C("browser.prepare") with
            {
                Id = taskId,
                ExpectedVersion = store.Work(taskId).Version,
                Body = CompanyJson.Encode(
                    new CompanyBrowserInput(
                        "Review fictional services",
                        ["https://paper.example.invalid"],
                        [new("navigate", "https://paper.example.invalid/services"), new("read", "body")],
                        new(5),
                        Screenshots: false
                    )
                ),
            }
        );

    private static void StartWebsite(CompanyStore store, CompanyBrowserJob job)
    {
        if (store.ComputeUsage().Policy.Limits.Paused)
            store.Apply(
                C("compute.configure") with
                {
                    ExpectedVersion = store.ComputeUsage().Policy.Version,
                    Body = CompanyJson.Encode(new CompanyComputeLimits(5, 100, false)),
                }
            );
        store.Apply(
            C("browser.run") with
            {
                Id = job.Id,
                ExpectedVersion = job.Version,
                Digest = job.Digest,
                Decision = "approve-once",
            }
        );
    }

    private static CompanyCommand WebsiteOutput(CompanyBrowserJob job) =>
        C("browser.output.prepare") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.ResultDigest,
            Decision = "prepare-review",
            Body = CompanyJson.Encode(
                new CompanyBrowserArtifactInput(2, job.Execution!.Result!.Captures.Single().Text)
            ),
        };

    private static CompanyArtifact CompleteWebsite(CompanyStore store, CompanyBrowserJob job)
    {
        var result = A<CompanyArtifact>(store, WebsiteOutput(job));
        Assert.Equal("needs_review", result.State);
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.complete") with { Id = job.TaskId, ExpectedVersion = store.Work(job.TaskId).Version })
        );
        result = Review(store, result, "approve");
        store.Apply(C("task.complete") with { Id = job.TaskId, ExpectedVersion = store.Work(job.TaskId).Version });
        return result;
    }

    [Fact]
    public async Task PlannedBrowserCompletesThroughReviewAndKeepsRevocableHistoryAfterItsDeadline()
    {
        var store = Store(browser: true);
        var plan = Approve(store, Plan(store).Plan);
        Assert.Throws<WorkspaceException>(() => PrepareWebsite(store, plan.TaskIds[1]));
        var prior = ApprovedPredecessor(store, plan);
        var job = PrepareWebsite(store, plan.TaskIds[1]);
        Assert.Equal(plan.Id, job.Plan!.Id);
        Assert.Equal(prior.Id, Assert.Single(Assert.Single(job.Plan.Dependencies).Artifacts).Id);
        Assert.DoesNotContain("Private", CompanyJson.Encode(job.Request));
        Assert.DoesNotContain(prior.Body, CompanyJson.Encode(job.Request));
        Assert.Equal(0, store.ComputeUsage().Jobs);
        StartWebsite(store, job);
        var executor = new BrowserFixtureExecutor();
        job = await new CompanyBrowserRunner(store, executor).RunAsync(job.Id, TestContext.Current.CancellationToken);
        var outputCommand = WebsiteOutput(job);
        var result = CompleteWebsite(store, job);
        Assert.Equal("completed", Items<CompanyPlan>(store, "plans").Single(p => p.Id == plan.Id).State);
        var budget = Items<CompanyBudget>(store, "budgets").Single(b => b.Id == plan.RootId);
        Assert.Equal(44000, budget.Limit);
        Assert.Equal(0, budget.Used);
        Assert.Equal(17, store.ComputeUsage().AllocatedSeconds);
        Assert.Equal(1, executor.Executions);
        _now = _now.AddDays(8);
        var reopened = Store(browser: true);
        Assert.Equal(
            result.Id,
            A<CompanyArtifact>(reopened, outputCommand with { RequestId = Guid.NewGuid().ToString("D") }).Id
        );
        Review(reopened, prior, "revoke");
        Assert.Throws<WorkspaceException>(() =>
            reopened.Apply(outputCommand with { RequestId = Guid.NewGuid().ToString("D") })
        );
        Assert.True(reopened.VerifyAudit().Intact);
    }

    [Fact]
    public async Task ReviewedBrowserEvidenceFeedsAPlanScriptAndRevokesThroughDeletedRawCaptures()
    {
        var store = Store(sandbox: true, browser: true);
        var plan = Approve(store, Plan(store).Plan);
        var browser = PrepareWebsite(store, plan.TaskIds[0]);
        StartWebsite(store, browser);
        browser = await new CompanyBrowserRunner(store, new BrowserFixtureExecutor()).RunAsync(
            browser.Id,
            TestContext.Current.CancellationToken
        );
        var prior = CompleteWebsite(store, browser);
        var script = PrepareScript(store, plan.TaskIds[1], prior.Id);
        Assert.Equal(prior.Body, Assert.Single(script.Request.Inputs).Text);
        StartScript(store, script);
        script = await new CompanySandboxRunner(store, new SandboxFixtureExecutor()).RunAsync(
            script.Id,
            TestContext.Current.CancellationToken
        );
        var output = A<CompanyArtifact>(store, ScriptOutput(script));
        Review(store, output, "approve");
        store.Apply(
            C("task.complete") with
            {
                Id = script.TaskId,
                ExpectedVersion = store.Work(script.TaskId).Version,
            }
        );
        browser = A<CompanyBrowserJob>(
            store,
            C("browser.captures.delete") with
            {
                Id = browser.Id,
                ExpectedVersion = browser.Version,
                Digest = browser.ResultDigest,
                Decision = "delete-captures",
            }
        );
        Assert.NotNull(browser.CaptureDeletion);
        Assert.Equal(output.Id, A<CompanyArtifact>(store, ScriptOutput(script)).Id);
        store.Apply(C("browser.revoke") with { Id = browser.Id, ExpectedVersion = browser.Version });
        Assert.Throws<WorkspaceException>(() => store.Apply(ScriptOutput(script)));
        Assert.Equal(22, store.ComputeUsage().AllocatedSeconds);
    }

    [Fact]
    public void BrowserAndModelAdmissionExcludeEachOtherInEitherOrder()
    {
        foreach (var modelFirst in new[] { false, true })
        {
            var store = Store(modelFirst ? "model-first" : "browser-first", browser: true);
            var plan = Approve(store, Plan(store).Plan);
            var browser = PrepareWebsite(store, plan.TaskIds[0]);
            if (modelFirst)
            {
                Prepare(store, browser.TaskId);
                Assert.Throws<WorkspaceException>(() => StartWebsite(store, browser));
                Assert.Throws<WorkspaceException>(() => PrepareWebsite(store, browser.TaskId));
                Assert.Equal(0, store.ComputeUsage().Jobs);
            }
            else
            {
                StartWebsite(store, browser);
                Assert.Throws<WorkspaceException>(() => Prepare(store, browser.TaskId));
                Assert.Equal(17, store.ComputeUsage().HeldSeconds);
            }
        }
    }

    [Fact]
    public async Task IsolatedJobsExcludeCompetingWorkUntilTheirResourcesAreReleased()
    {
        foreach (var browserFirst in new[] { false, true })
        {
            var store = Store(browserFirst ? "browser-first" : "script-first", sandbox: true, browser: true);
            var plan = Approve(store, Plan(store).Plan);
            var browser = PrepareWebsite(store, plan.TaskIds[0]);
            var anotherBrowser = PrepareWebsite(store, browser.TaskId);
            var script = PrepareScript(store, browser.TaskId);
            var anotherScript = PrepareScript(store, browser.TaskId);
            if (browserFirst)
            {
                StartWebsite(store, browser);
                var executor = new BrowserFixtureExecutor { ReleaseFailures = 1 };
                var runner = new CompanyBrowserRunner(store, executor);
                browser = await runner.RunAsync(browser.Id, TestContext.Current.CancellationToken);
                Assert.Null(browser.SettledAt);
                Assert.Throws<WorkspaceException>(() => StartScript(store, script));
                Assert.Throws<WorkspaceException>(() => StartWebsite(store, anotherBrowser));
                Assert.Throws<WorkspaceException>(() => Prepare(store, browser.TaskId));
                browser = await runner.RecoverAsync(browser.Id, TestContext.Current.CancellationToken);
                Assert.NotNull(browser.SettledAt);
                StartScript(store, script);
                Assert.Equal(1, executor.Executions);
            }
            else
            {
                StartScript(store, script);
                Assert.Throws<WorkspaceException>(() => StartWebsite(store, browser));
                Assert.Throws<WorkspaceException>(() => StartScript(store, anotherScript));
                await new CompanySandboxRunner(store, new SandboxFixtureExecutor()).RunAsync(
                    script.Id,
                    TestContext.Current.CancellationToken
                );
                StartWebsite(store, browser);
            }
            Assert.Equal(2, store.ComputeUsage().Jobs);
        }
    }

    [Fact]
    public void PlanCancellationOrExpiryWithdrawsQueuedBrowserAuthorityWithoutReleasingItsHold()
    {
        foreach (var expiry in new[] { false, true })
        {
            var store = Store(expiry ? "expires" : "cancels", browser: true);
            var plan = Approve(store, Plan(store).Plan);
            var job = PrepareWebsite(store, plan.TaskIds[0]);
            StartWebsite(store, job);
            if (expiry)
                _now = _now.AddDays(8);
            else
                store.Apply(C("plan.cancel") with { Id = plan.Id, ExpectedVersion = plan.Version });
            Assert.Null(store.TakeBrowserDispatch(job.Id, job.RuntimeIdentity));
            Assert.Throws<WorkspaceException>(() => PrepareWebsite(store, job.TaskId));
            Assert.Equal(17, store.ComputeUsage().HeldSeconds + store.ComputeUsage().CarriedSeconds);
        }
    }

    [Fact]
    public async Task RevokingPredecessorWithholdsLateBrowserCapturesAndRetainsCleanupHold()
    {
        var store = Store(browser: true);
        var plan = Approve(store, Plan(store).Plan);
        var prior = ApprovedPredecessor(store, plan);
        var job = PrepareWebsite(store, plan.TaskIds[1]);
        StartWebsite(store, job);
        Assert.NotNull(store.TakeBrowserDispatch(job.Id, job.RuntimeIdentity));
        var receipt = await new BrowserFixtureExecutor().ExecuteAsync(
            job.Request,
            () => store.BrowserAuthorized(job.Id),
            TestContext.Current.CancellationToken
        );
        Review(store, prior, "revoke");
        Assert.False(store.BrowserAuthorized(job.Id));
        var saved = store.RecordBrowserResult(job.Id, job.RuntimeIdentity, receipt);
        Assert.Equal("withheld", saved.State);
        Assert.Null(saved.Execution!.Result);
        Assert.Equal(17, store.ComputeUsage().HeldSeconds);
    }

    [Fact]
    public void ChangedPredecessorOrSourceProposalRequiresFreshBrowserApproval()
    {
        var store = Store(browser: true);
        var (draft, source) = Plan(store);
        var plan = Approve(store, draft);
        var prior = ApprovedPredecessor(store, plan);
        var original = PrepareWebsite(store, plan.TaskIds[1]);
        Review(store, Review(store, prior, "revoke"), "approve");
        Assert.Throws<WorkspaceException>(() => StartWebsite(store, original));
        var fresh = PrepareWebsite(store, plan.TaskIds[1]);
        Review(store, source, "revoke");
        Assert.Throws<WorkspaceException>(() => StartWebsite(store, fresh));
        Assert.Equal(0, store.ComputeUsage().Jobs);
    }

    [Fact]
    public void CompletingAPlanChildDoesNotAuthorizeItsPreviouslyPreparedBrowser()
    {
        var store = Store(browser: true);
        var plan = Approve(store, Plan(store).Plan);
        var job = PrepareWebsite(store, plan.TaskIds[0]);
        ApprovedPredecessor(store, plan);
        Assert.Throws<WorkspaceException>(() => StartWebsite(store, job));
        Assert.Equal(0, store.ComputeUsage().Jobs);
    }
}
