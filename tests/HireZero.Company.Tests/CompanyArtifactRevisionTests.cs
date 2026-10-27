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
    public void ASecondRevisionCarriesOnlyTheImmediatelyPreviousDraftAndNeedsAnotherAuthorization()
    {
        var store = Store();
        var original = MeasuredQueueChecks.Seed(store);
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
        Assert.Equal(2, MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts").Length);
        Assert.Equal("proposed", prepared.Measured!.State);
        Assert.True(store.VerifyAudit().Intact);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
