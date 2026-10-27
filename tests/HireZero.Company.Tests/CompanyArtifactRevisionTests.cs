using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyArtifactRevisionTests : IDisposable
{
    private readonly TemporaryRoot _root = new("company-artifact-revision-");

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-revision-runtime",
            companyId: company
        );

    private static CompanyCommand C(string action) => MeasuredQueueChecks.C(action);

    private static CompanyTask Deliverable(CompanyStore store)
    {
        var seeded = MeasuredQueueChecks.Seed(store);
        return Apply<CompanyTask>(
            store,
            C("task.create") with
            {
                EmployeeId = seeded.EmployeeId,
                ThreadId = seeded.ThreadId,
                Skill = "marketing-starter-plan",
                Body = "Prepare a complete fictional paper-folding plan with three usable drafts.",
            }
        );
    }

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static CompanyAttempt Prepare(CompanyStore store, CompanyTask task) =>
        Apply<CompanyAttempt>(
            store,
            C("measured.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                CreditAllowance = 1,
            }
        );

    private static CompanyCommand Run(CompanyAttempt attempt) =>
        C("measured.run") with
        {
            Id = attempt.Id,
            ExpectedVersion = attempt.Version,
            Digest = attempt.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    private static CompanyArtifact Finish(CompanyStore store, CompanyTask task, string body)
    {
        var prepared = Prepare(store, task);
        store.Apply(Run(prepared));
        store.Finish(prepared.Id, new ModelReply(body, 20, "fictional-revision-" + task.Id, new(10, 10)));
        return MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts").Single(a => a.TaskId == task.Id);
    }

    private static CompanyCommand Revision(CompanyArtifact artifact) =>
        C("artifact.revise") with
        {
            Id = artifact.Id,
            ExpectedVersion = artifact.Version,
            Digest = artifact.Digest,
            Body =
                "Keep the folding tip. Replace the generic promotion with one concrete paper-crane experiment and three usable drafts.",
        };

    [Fact]
    public void FeedbackPersistsAsASeparateBoundedRevisionWithoutDispatchOrApproval()
    {
        var store = Store();
        var original = MeasuredQueueChecks.Seed(store);
        var artifact = Finish(
            store,
            original,
            "## First draft\nA generic fictional paper-crane promotion. Keep this folding tip."
        );
        var request = Revision(artifact);
        var reply = store.Apply(request);
        var revision = CompanyJson.Decode<CompanyTask>(reply);
        Assert.Equal(reply, store.Apply(request, out var replayed));
        Assert.False(replayed);
        Assert.Equal("blocked", revision.State);
        Assert.Equal("revision-awaiting-approval", revision.Reason);
        Assert.Equal("superseded", store.Work(original.Id).State);
        Assert.Equal(original.Input, revision.Input);
        Assert.Equal(original.ModelRoute, revision.RequestedModelRoute);
        Assert.Single(MeasuredQueueChecks.Items<System.Text.Json.JsonElement>(store, "measured"));
        Assert.Null(store.Claim(original.EmployeeId, true));
        Assert.Null(store.Claim(original.EmployeeId, true, revision.Id));
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.retry") with { Id = revision.Id, ExpectedVersion = revision.Version })
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("task.reassign") with
                {
                    Id = revision.Id,
                    ExpectedVersion = revision.Version,
                    EmployeeId = original.EmployeeId,
                }
            )
        );
        var day = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        Assert.Contains(
            store.Today(day.ToString("yyyy-MM-dd"), day, day.AddDays(1)).NeedsYou,
            need => need.Kind == "revision" && need.TaskId == revision.Id
        );
        Assert.Equal(
            0,
            MeasuredQueueChecks.Items<CompanyBudget>(store, "budgets").Single(b => b.Id == revision.RootId).Reserved
        );
        var retained = MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts").Single();
        Assert.Equal(artifact.Body, retained.Body);
        Assert.Equal("changes_requested", retained.State);
        Assert.Equal(request.Body, retained.ReviewFeedback);
        Assert.Empty(store.Memories());

        store = Store();
        revision = store.Work(revision.Id);
        var prepared = Prepare(store, revision);
        var call = store.TextCall(prepared.Id);
        Assert.Equal(artifact.Body, call.Context.Revision!.OriginalDraft);
        Assert.Equal(request.Body, call.Context.Revision.Request.Feedback);
        Assert.Equal(revision.Revision, call.Context.Revision.Request);
        Assert.Null(call.Context.Revision.PriorFeedback);
        Assert.DoesNotContain("priorFeedback", CompanyJson.Encode(call.Context.Revision));
        Assert.Empty(call.Context.History);
        Assert.DoesNotContain(call.Context.Evidence, e => e.Id == artifact.Id);
        Assert.Contains("previous unapproved draft", call.Instructions);
        Assert.Contains("complete replacement deliverable", call.Instructions);
        Assert.Equal("proposed", prepared.Measured!.State);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void InvalidFeedbackDigestAndCrossCompanyRequestsLeaveTheOriginalUntouched()
    {
        var store = Store();
        var original = MeasuredQueueChecks.Seed(store);
        var artifact = Finish(store, original, "Fictional original draft.");
        foreach (
            var request in new[]
            {
                Revision(artifact) with
                {
                    Body = " ",
                },
                Revision(artifact) with
                {
                    Digest = "wrong",
                },
                Revision(artifact) with
                {
                    ExpectedVersion = 9,
                },
            }
        )
            Assert.Throws<WorkspaceException>(() => store.Apply(request));
        Assert.Throws<WorkspaceException>(() => Store("fictional-other").Apply(Revision(artifact)));
        Assert.Single(MeasuredQueueChecks.Items<CompanyTask>(store, "tasks"));
        Assert.Equal("needs_review", MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts").Single().State);
        Assert.Equal("awaiting_review", store.Work(original.Id).State);
    }

    [Fact]
    public void RevokedOriginalKnowledgeInvalidatesThePreparedRevisionBeforeDispatch()
    {
        var store = Store();
        var original = MeasuredQueueChecks.Seed(store);
        var artifact = Finish(store, original, "Fictional original based on approved paper-kit facts.");
        var revision = Apply<CompanyTask>(store, Revision(artifact));
        var prepared = Prepare(store, revision);
        var knowledge = MeasuredQueueChecks
            .Items<CompanyKnowledge>(store, "knowledge")
            .Single(k => k.Id == original.KnowledgeId);
        store.Apply(
            C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "revoke",
            }
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(prepared)));
        Assert.Single(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        Assert.Equal(
            0,
            MeasuredQueueChecks.Items<CompanyBudget>(store, "budgets").Single(b => b.Id == revision.RootId).Reserved
        );
    }

    [Fact]
    public void LaterRevisionsKeepEarlierFeedbackButOnlyTheImmediatelyPreviousDraft()
    {
        var store = Store();
        var original = Deliverable(store);
        var first = Finish(store, original, "Fictional first draft with first-only phrase.");
        var revision = Apply<CompanyTask>(store, Revision(first));
        var second = Finish(store, revision, "Fictional replacement with second-only phrase.");
        var next = Apply<CompanyTask>(
            store,
            Revision(second) with
            {
                Body = "Keep the concrete example. Make the final question more specific.",
            }
        );
        var prepared = Prepare(store, next);
        var call = store.TextCall(prepared.Id);
        Assert.Equal(second.Body, call.Context.Revision!.OriginalDraft);
        Assert.DoesNotContain("first-only phrase", call.Context.Revision.OriginalDraft);
        Assert.Equal(revision.Revision, Assert.Single(call.Context.Revision.PriorFeedback!));
        Assert.Equal(next.Revision!.Feedback, call.Context.Revision.Request.Feedback);
        Assert.Contains("oldest first", call.Instructions);
        Assert.Empty(call.Context.History);
        Assert.Equal(2, MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts").Length);
        Assert.Equal("proposed", prepared.Measured!.State);
        store.Apply(Run(prepared));
        store.Finish(
            prepared.Id,
            new ModelReply("Fictional third draft with third-only phrase.", 20, "fictional-third", new(10, 10))
        );
        var third = MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts").Single(a => a.TaskId == next.Id);
        var last = Apply<CompanyTask>(
            store,
            Revision(third) with
            {
                Body = "Remove the final question; keep the earlier concrete experiment and three drafts.",
            }
        );
        store = Store();
        var lastPrepared = Prepare(store, store.Work(last.Id));
        var finalCall = store.TextCall(lastPrepared.Id);
        Assert.Equal(new[] { revision.Revision, next.Revision }, finalCall.Context.Revision!.PriorFeedback);
        Assert.Equal(third.Body, finalCall.Context.Revision.OriginalDraft);
        Assert.Equal(last.Revision!.Feedback, finalCall.Context.Revision.Request.Feedback);
        Assert.DoesNotContain("first-only phrase", CompanyJson.Encode(finalCall.Context));
        Assert.DoesNotContain("second-only phrase", CompanyJson.Encode(finalCall.Context));
        Assert.Equal("proposed", lastPrepared.Measured!.State);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void OversizedFeedbackHistoryIsRefusedWithoutDroppingEarlierRequirementsOrDispatching()
    {
        var store = Store();
        var task = Deliverable(store);
        for (var i = 0; i < 7; i++)
        {
            var artifact = Apply<CompanyArtifact>(
                store,
                C("artifact.create") with
                {
                    Id = task.Id,
                    ExpectedVersion = store.Work(task.Id).Version,
                    Body = "Fictional manually revised folding instruction.",
                }
            );
            task = Apply<CompanyTask>(
                store,
                Revision(artifact) with
                {
                    Body = $"Owner requirement {i}: " + new string('x', 3850),
                }
            );
        }
        Assert.Throws<WorkspaceException>(() => Prepare(store, task));
        Assert.Equal("blocked", store.Work(task.Id).State);
        Assert.Equal(7, MeasuredQueueChecks.Items<CompanyTask>(store, "tasks").Count(t => t.Revision is not null));
        Assert.Empty(MeasuredQueueChecks.Items<System.Text.Json.JsonElement>(store, "measured"));
        Assert.True(store.VerifyAudit().Intact);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
