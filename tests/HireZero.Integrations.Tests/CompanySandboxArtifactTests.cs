using HireZero.Core;
using HireZero.Integrations.Sandbox;

public sealed partial class CompanySandboxTests
{
    private static CompanyCommand OutputReview(CompanySandboxJob job, string quote = "Paper: 3") =>
        C("sandbox.output.prepare") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.ResultDigest,
            Decision = "prepare-review",
            Body = CompanyJson.Encode(new CompanySandboxArtifactInput("report.txt", quote)),
        };

    private static CompanyArtifact ApproveOutput(CompanyStore store, CompanyArtifact artifact) =>
        Apply<CompanyArtifact>(
            store,
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );

    private async Task<(
        CompanyTask Task,
        CompanyDocument Document,
        CompanySandboxJob Job,
        Executor Executor
    )> CompletedOutput(CompanyStore store)
    {
        Configure(store);
        var task = TaskFor(store);
        var document = Document(store, task.EmployeeId);
        var job = Propose(store, task, document);
        store.Apply(Start(job));
        var executor = new Executor(store) { ReleaseConfirmed = false };
        job = await new CompanySandboxRunner(store, executor).RunAsync(job.Id, Token);
        return (task, document, job, executor);
    }

    [Fact]
    public async Task SavedOutputBecomesOneReviewableResultAndCompletesWorkWithoutNewExecution()
    {
        var store = Store();
        var (task, _, job, executor) = await CompletedOutput(store);
        Assert.Null(job.SettledAt);
        var command = OutputReview(job);
        var artifact = Apply<CompanyArtifact>(Store(runtime: null), command);
        Assert.Equal("needs_review", artifact.State);
        Assert.Equal("sandbox-output", artifact.Provenance);
        Assert.Equal("Paper: 3", artifact.Body);
        Assert.Equal(job.Id, artifact.SandboxOrigin!.JobId);
        Assert.Equal(job.ResultDigest, artifact.SandboxOrigin.ResultDigest);
        Assert.Equal("awaiting_review", store.Work(task.Id).State);
        Assert.Equal(artifact.Id, Apply<CompanyArtifact>(Store(), command).Id);
        Assert.Equal(artifact.Id, Apply<CompanyArtifact>(Store(), OutputReview(job)).Id);
        Assert.Single(Items<CompanyArtifact>(store, "artifacts"));
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version })
        );
        ApproveOutput(store, artifact);
        store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version });
        Assert.Equal("completed", store.Work(task.Id).State);
        Assert.Throws<WorkspaceException>(() => Propose(store, store.Work(task.Id)));
        Assert.Equal(1, executor.Executions);
        Assert.Equal(5, store.ComputeUsage().HeldSeconds);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public async Task OutputReviewRequiresOwnerExactSavedTextAndCompanyScope()
    {
        var store = Store();
        var (_, _, job, _) = await CompletedOutput(store);
        var command = OutputReview(job);
        Assert.Throws<WorkspaceException>(() => store.Apply(command with { Digest = "changed" }));
        Assert.Throws<WorkspaceException>(() => store.Apply(OutputReview(job, "Edited result")));
        Assert.Throws<WorkspaceException>(() => store.Apply(OutputReview(job, new string('x', 12001))));
        Assert.Throws<WorkspaceException>(() => Store("foreign-company").Apply(command));
        store.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = CompanyRoles.Approver });
        store.SignIn("fictional-reviewer", "reviewer@example.invalid", true, "Reviewer");
        Assert.Throws<WorkspaceException>(() => store.Apply(command, "fictional-reviewer", out _));
        Assert.Empty(Items<CompanyArtifact>(store, "artifacts"));
        Assert.Empty(Items<CompanyArtifact>(Store("foreign-company"), "artifacts"));
    }

    [Fact]
    public async Task RevokingAnInputFencesResultReviewHandoffsMemoryDispatchAndPublication()
    {
        var store = Store(publishing: true);
        var (task, document, job, _) = await CompletedOutput(store);
        var artifact = ApproveOutput(store, Apply<CompanyArtifact>(store, OutputReview(job)));
        var recipient = TaskFor(store);
        var dependent = Apply<CompanyTask>(
            store,
            C("task.create") with
            {
                EmployeeId = recipient.EmployeeId,
                ThreadId = recipient.ThreadId,
                Skill = "report",
                Body = "Use the approved paper count",
                Evidence = [artifact.Id],
            }
        );
        var attempt = store.Claim(recipient.EmployeeId, true, dependent.Id)!;
        Assert.NotNull(attempt);
        Assert.Equal(artifact.Id, Assert.Single(store.Capture(attempt.Id).Evidence).Id);
        var handoff = C("handoff.assign") with
        {
            Id = artifact.Id,
            ExpectedVersion = artifact.Version,
            Digest = artifact.Digest,
            EmployeeId = recipient.EmployeeId,
            ThreadId = recipient.ThreadId,
            Skill = "report",
            Body = "Review the paper count",
        };
        store.Apply(handoff);
        var memory = Apply<CompanyMemoryItem>(
            store,
            C("memory.save") with
            {
                Body = CompanyJson.Encode(
                    new CompanyMemoryInput(
                        "prior-work",
                        "Fictional paper count",
                        "The reviewed report counted paper",
                        "Keep the source attached",
                        ArtifactId: artifact.Id,
                        Quote: artifact.Body
                    )
                ),
            }
        );
        store.Apply(
            C("memory.review") with
            {
                Id = memory.Id,
                ExpectedVersion = memory.Version,
                Digest = memory.Digest,
                Decision = "approve",
            }
        );
        var publication = store.ProposePublication(
            new(null, artifact.Id, "Fictional reviewed destination", [new string('a', 64)])
        );
        store.Apply(
            C("publication.review") with
            {
                Id = publication.Id,
                ExpectedVersion = publication.Version,
                Digest = publication.Digest,
                Decision = "approve",
            }
        );
        store.Apply(
            C("document.review") with
            {
                Id = document.Id,
                ExpectedVersion = document.Version,
                Digest = document.Digest,
                Decision = "revoke",
            }
        );
        Assert.False(store.IsCurrent(attempt.Id));
        Assert.False(store.Memory(memory.Id).Usable);
        Assert.Throws<WorkspaceException>(() => store.Apply(handoff with { RequestId = Guid.NewGuid().ToString("D") }));
        Assert.Throws<WorkspaceException>(() => ApproveOutput(store, artifact));
        Assert.Throws<WorkspaceException>(() => store.ClaimPublication(publication.Id));
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version })
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(OutputReview(job, "Paper")));
        Assert.Equal(artifact.Body, store.Artifact(artifact.Id).Body); // Historical evidence remains visible, without new authority.
    }

    [Fact]
    public async Task RevokedScriptCannotBeLaunderedThroughAnApprovedResult()
    {
        var store = Store();
        var (_, _, job, _) = await CompletedOutput(store);
        var artifact = ApproveOutput(store, Apply<CompanyArtifact>(store, OutputReview(job)));
        store.Apply(C("sandbox.revoke") with { Id = job.Id, ExpectedVersion = job.Version });
        var target = TaskFor(store);
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("task.create") with
                {
                    EmployeeId = target.EmployeeId,
                    ThreadId = target.ThreadId,
                    Skill = "report",
                    Body = "Reuse the revoked output",
                    Evidence = [artifact.Id],
                }
            )
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(OutputReview(job)));
        var revoked = Apply<CompanyArtifact>(
            store,
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "revoke",
            }
        );
        Assert.Equal("revoked", revoked.State);
    }

    [Fact]
    public async Task LongOutputsKeepTheWholeFileWhileReviewPinsAnExactBoundedExcerpt()
    {
        var store = Store();
        Configure(store);
        var job = Propose(store, TaskFor(store));
        store.Apply(Start(job));
        var text = "Header\n" + new string('x', 13000) + "\nPaper: 3";
        var executor = new Executor(store)
        {
            Execute = (_, _) =>
                System.Threading.Tasks.Task.FromResult(Success(job) with { Outputs = [new("report.txt", text)] }),
        };
        job = await new CompanySandboxRunner(store, executor).RunAsync(job.Id, Token);
        Assert.Throws<WorkspaceException>(() => store.Apply(OutputReview(job, text)));
        var artifact = Apply<CompanyArtifact>(store, OutputReview(job));
        Assert.Equal("Paper: 3", artifact.Body);
        Assert.Equal(text.IndexOf("Paper: 3", StringComparison.Ordinal), artifact.SandboxOrigin!.Start);
        Assert.Equal(text, Store().SandboxJob(job.Id).Result!.Outputs[0].Text);
    }

    [Fact]
    public async Task FailedAndWithheldOutputCannotBecomeTaskResults()
    {
        var store = Store();
        Configure(store);
        foreach (var revoked in new[] { false, true })
        {
            var job = Propose(store, TaskFor(store));
            store.Apply(Start(job));
            Assert.NotNull(store.TakeSandboxDispatch(job.Id, Runtime));
            if (revoked)
                store.Apply(C("sandbox.revoke") with { Id = job.Id, ExpectedVersion = job.Version });
            job = store.RecordSandboxResult(
                job.Id,
                Runtime,
                revoked ? Success(job) : Success(job) with { State = "failed", ExitCode = 1 }
            );
            Assert.Equal(revoked ? "withheld" : "failed", job.State);
            Assert.Throws<WorkspaceException>(() => store.Apply(OutputReview(job)));
        }
        Assert.Empty(Items<CompanyArtifact>(store, "artifacts"));
    }
}
