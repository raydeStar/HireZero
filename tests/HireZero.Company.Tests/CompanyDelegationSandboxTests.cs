using HireZero.Core;
using HireZero.Integrations.Sandbox;

public sealed partial class CompanyDelegationTests
{
    private static CompanyArtifact ApprovedPredecessor(CompanyStore store, CompanyPlan plan)
    {
        var task = store.Work(plan.TaskIds[0]);
        var artifact = A<CompanyArtifact>(
            store,
            C("artifact.create") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = "category,count\nPaper,3\nWorkshop,2",
            }
        );
        artifact = Review(store, artifact, "approve");
        store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version });
        return artifact;
    }

    private static CompanySandboxJob PrepareScript(CompanyStore store, string taskId, params string[] artifacts)
    {
        var task = store.Work(taskId);
        return A<CompanySandboxJob>(
            store,
            C("sandbox.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = CompanyJson.Encode(
                    new CompanySandboxInput(
                        "Summarize approved results",
                        "console.log('Fictional script; never evaluated by this test')",
                        [],
                        ["report.txt"],
                        new(5),
                        artifacts
                            .Select((id, index) => new CompanySandboxEvidenceSelection(id, $"result-{index + 1}.txt"))
                            .ToArray()
                    )
                ),
            }
        );
    }

    private static void StartScript(CompanyStore store, CompanySandboxJob job)
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
            C("sandbox.run") with
            {
                Id = job.Id,
                ExpectedVersion = job.Version,
                Digest = job.Digest,
                Decision = "approve-once",
            }
        );
    }

    private static CompanyCommand ScriptOutput(CompanySandboxJob job) =>
        C("sandbox.output.prepare") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.ResultDigest,
            Decision = "prepare-review",
            Body = CompanyJson.Encode(new CompanySandboxArtifactInput("report.txt", job.Result!.Outputs.Single().Text)),
        };

    [Fact]
    public async Task PlannedScriptUsesOnlySelectedApprovedResultsAndCompletesItsParent()
    {
        var store = Store(sandbox: true);
        var plan = Approve(store, Plan(store).Plan);
        Assert.Throws<WorkspaceException>(() => PrepareScript(store, plan.TaskIds[1]));
        var prior = ApprovedPredecessor(store, plan);
        Assert.Equal(prior.Id, Assert.Single(store.SandboxEvidenceOptions(plan.TaskIds[1])).Id);
        var job = PrepareScript(store, plan.TaskIds[1], prior.Id);
        Assert.Equal(plan.Id, job.Plan!.Id);
        Assert.Equal(prior.Body, Assert.Single(job.Request.Inputs).Text);
        Assert.DoesNotContain("Private marketing", CompanyJson.Encode(job.Request));
        Assert.DoesNotContain("Private developer", CompanyJson.Encode(job.Request));
        Assert.Equal(0, store.ComputeUsage().Jobs);
        StartScript(store, job);
        var executor = new SandboxFixtureExecutor();
        job = await new CompanySandboxRunner(store, executor).RunAsync(job.Id, TestContext.Current.CancellationToken);
        Assert.Equal(CompanyJson.Encode(job.Request), CompanyJson.Encode(executor.LastRequest));
        var result = A<CompanyArtifact>(store, ScriptOutput(job));
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.complete") with { Id = job.TaskId, ExpectedVersion = store.Work(job.TaskId).Version })
        );
        Review(store, result, "approve");
        store.Apply(C("task.complete") with { Id = job.TaskId, ExpectedVersion = store.Work(job.TaskId).Version });
        Assert.Equal("completed", Items<CompanyPlan>(store, "plans").Single(p => p.Id == plan.Id).State);
        Assert.Equal(44000, Items<CompanyBudget>(store, "budgets").Single(b => b.Id == plan.RootId).Limit);
        Assert.Equal(0, Items<CompanyBudget>(store, "budgets").Single(b => b.Id == plan.RootId).Used);
        Assert.Equal(5, store.ComputeUsage().AllocatedSeconds);
        Assert.Equal(1, executor.Executions);
        _now = _now.AddDays(8);
        Assert.Equal(result.Id, A<CompanyArtifact>(Store(sandbox: true), ScriptOutput(job)).Id);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void APlanTaskCannotStartModelAndScriptWorkConcurrently()
    {
        var store = Store(sandbox: true);
        var plan = Approve(store, Plan(store).Plan);
        ApprovedPredecessor(store, plan);
        var job = PrepareScript(store, plan.TaskIds[1]);
        StartScript(store, job);
        Assert.Throws<WorkspaceException>(() => Prepare(store, plan.TaskIds[1]));
        var other = Store("model-first", sandbox: true);
        var otherPlan = Approve(other, Plan(other).Plan);
        ApprovedPredecessor(other, otherPlan);
        var pending = PrepareScript(other, otherPlan.TaskIds[1]);
        Prepare(other, otherPlan.TaskIds[1]);
        Assert.Throws<WorkspaceException>(() => StartScript(other, pending));
        Assert.Throws<WorkspaceException>(() => PrepareScript(other, otherPlan.TaskIds[1]));
        Assert.Equal(0, other.ComputeUsage().Jobs);
    }

    [Fact]
    public void RevokingMeasuredPredecessorContextInvalidatesItsSelectedScriptInput()
    {
        var store = Store(sandbox: true);
        var plan = Approve(store, Plan(store).Plan);
        var attempt = Prepare(store, plan.TaskIds[0]);
        store.Apply(Run(attempt));
        Finish(store, attempt);
        var prior = Complete(store, attempt);
        var job = PrepareScript(store, plan.TaskIds[1], prior.Id);
        var knowledge = Items<CompanyKnowledge>(store, "knowledge")
            .Single(k => k.Id == store.Work(plan.TaskIds[0]).KnowledgeId);
        store.Apply(
            C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "revoke",
            }
        );
        Assert.Empty(store.SandboxEvidenceOptions(plan.TaskIds[1]));
        Assert.Throws<WorkspaceException>(() => StartScript(store, job));
        Assert.Equal(0, store.ComputeUsage().Jobs);
    }

    [Fact]
    public void CompletingAPlanChildDoesNotAuthorizeItsPreviouslyPreparedScript()
    {
        var store = Store(sandbox: true);
        var plan = Approve(store, Plan(store).Plan);
        var pending = PrepareScript(store, plan.TaskIds[0]);
        ApprovedPredecessor(store, plan);
        Assert.Throws<WorkspaceException>(() => StartScript(store, pending));
        Assert.Equal(0, store.ComputeUsage().Jobs);
    }

    [Fact]
    public void PlannedScriptRefusesOtherCompanyAndUnrelatedArtifactInputs()
    {
        var store = Store(sandbox: true);
        var (draft, proposal) = Plan(store);
        var plan = Approve(store, draft);
        var prior = ApprovedPredecessor(store, plan);
        Assert.Throws<WorkspaceException>(() => PrepareScript(store, plan.TaskIds[1], proposal.Id));
        Assert.Throws<WorkspaceException>(() =>
            PrepareScript(Store("foreign-company", sandbox: true), plan.TaskIds[1], prior.Id)
        );
        Assert.Throws<WorkspaceException>(() =>
            Store("foreign-company", sandbox: true).SandboxEvidenceOptions(plan.TaskIds[1])
        );
        Assert.Empty(PrepareScript(store, plan.TaskIds[1]).Request.Inputs);
    }

    [Fact]
    public void PlanCancellationOrExpiryWithdrawsQueuedScriptAuthority()
    {
        foreach (var expiry in new[] { false, true })
        {
            var store = Store(expiry ? "expires" : "cancels", sandbox: true);
            var plan = Approve(store, Plan(store).Plan);
            var prior = ApprovedPredecessor(store, plan);
            var job = PrepareScript(store, plan.TaskIds[1], prior.Id);
            StartScript(store, job);
            if (expiry)
                _now = _now.AddDays(8);
            else
                store.Apply(C("plan.cancel") with { Id = plan.Id, ExpectedVersion = plan.Version });
            Assert.Null(store.TakeSandboxDispatch(job.Id, job.RuntimeIdentity));
            Assert.Throws<WorkspaceException>(() => PrepareScript(store, plan.TaskIds[1], prior.Id));
            Assert.Equal(5, store.ComputeUsage().HeldSeconds + store.ComputeUsage().CarriedSeconds);
        }
    }

    [Fact]
    public void RevokingPredecessorWithholdsLateScriptOutputAndKeepsItsResourceHold()
    {
        var store = Store(sandbox: true);
        var plan = Approve(store, Plan(store).Plan);
        var prior = ApprovedPredecessor(store, plan);
        var job = PrepareScript(store, plan.TaskIds[1], prior.Id);
        StartScript(store, job);
        Assert.NotNull(store.TakeSandboxDispatch(job.Id, job.RuntimeIdentity));
        Review(store, prior, "revoke");
        Assert.False(store.SandboxAuthorized(job.Id));
        var saved = store.RecordSandboxResult(
            job.Id,
            job.RuntimeIdentity,
            new(
                "completed",
                0,
                "private",
                "",
                [new("report.txt", "Untrusted late result")],
                12,
                new(false, "awaiting-release", "fictional-" + job.Id),
                CreationConfirmed: true
            )
        );
        Assert.Equal("withheld", saved.State);
        Assert.Empty(saved.Result!.Outputs);
        Assert.Equal(5, store.ComputeUsage().HeldSeconds);
        Assert.Empty(store.SandboxEvidenceOptions(plan.TaskIds[1]));
    }

    [Fact]
    public void AChangedPredecessorRevisionOrSourceProposalRequiresFreshScriptApproval()
    {
        var store = Store(sandbox: true);
        var (draft, source) = Plan(store);
        var plan = Approve(store, draft);
        var prior = ApprovedPredecessor(store, plan);
        var original = PrepareScript(store, plan.TaskIds[1], prior.Id);
        prior = Review(store, Review(store, prior, "revoke"), "approve");
        Assert.Throws<WorkspaceException>(() => StartScript(store, original));
        var fresh = PrepareScript(store, plan.TaskIds[1], prior.Id);
        Review(store, source, "revoke");
        Assert.Throws<WorkspaceException>(() => StartScript(store, fresh));
        Assert.Equal(0, store.ComputeUsage().Jobs);
    }
}
