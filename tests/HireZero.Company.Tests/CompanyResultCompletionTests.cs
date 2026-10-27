using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyResultCompletionTests : IDisposable
{
    private readonly TemporaryRoot _root = new("company-result-completion-");

    private CompanyStore Store() =>
        new(Path.Combine(_root.Path, "company.sqlite"), "owner@example.invalid", CompanyWorkspace.Definitions());

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static CompanyArtifact Draft(CompanyStore store, string taskId, string body) =>
        Apply<CompanyArtifact>(
            store,
            C("artifact.create") with
            {
                Id = taskId,
                ExpectedVersion = store.Work(taskId).Version,
                Body = body,
            }
        );

    private static CompanyNeed[] Needs(CompanyStore store, string taskId)
    {
        var now = DateTimeOffset.UtcNow;
        return store.Today("today", now.AddDays(-1), now.AddDays(1)).NeedsYou.Where(n => n.TaskId == taskId).ToArray();
    }

    [Fact]
    public void MultipleDraftsShareOneReviewNeedAndOneOwnerPriority()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        Draft(store, task.Id, "Fictional first folding explanation.");
        Draft(store, task.Id, "Fictional edited explanation with the missing fold.");

        var need = Assert.Single(Needs(store, task.Id));
        Assert.Equal("review", need.Kind);
        Assert.Contains("2", need.Detail);
        store.Apply(C("owner.mark") with { Id = need.Key, ExpectedVersion = 0, Decision = "high" });
        Draft(store, task.Id, "Fictional final wording for review.");

        var updated = Assert.Single(Needs(store, task.Id));
        Assert.Equal(need.Key, updated.Key);
        Assert.Equal("high", updated.Priority);
        Assert.Contains("3", updated.Detail);
    }

    [Fact]
    public void ApprovingOneCopyOffersCompletionWithoutApprovingEarlierDrafts()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        var original = Draft(store, task.Id, "Fictional initial copy; incomplete instruction.");
        var edited = Draft(store, task.Id, "Fictional corrected copy; fold the square in half.");
        store.Apply(
            C("artifact.review") with
            {
                Id = edited.Id,
                ExpectedVersion = edited.Version,
                Digest = edited.Digest,
                Decision = "approve",
            }
        );

        var ready = Assert.Single(Needs(store, task.Id));
        Assert.Equal("completion", ready.Kind);
        Assert.Contains("approved", ready.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(task.ThreadId, ready.ThreadId);
        store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version });

        var reopened = Store();
        Assert.Equal("completed", reopened.Work(task.Id).State);
        Assert.Empty(Needs(reopened, task.Id));
        var artifacts = MeasuredQueueChecks.Items<CompanyArtifact>(reopened, "artifacts");
        Assert.Equal(original, artifacts.Single(a => a.Id == original.Id));
        Assert.Equal("approved", artifacts.Single(a => a.Id == edited.Id).State);
        Assert.True(reopened.VerifyAudit().Intact);
    }

    [Fact]
    public void CancelledOrSupersededWorkDoesNotLeaveOldReviewNeeds()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        var canceledDraft = Draft(store, task.Id, "Fictional canceled paper draft.");
        store.Apply(C("task.cancel") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version });
        Assert.Empty(Needs(store, task.Id));

        var next = Apply<CompanyTask>(
            store,
            C("task.create") with
            {
                EmployeeId = task.EmployeeId,
                ThreadId = task.ThreadId,
                Skill = task.Skill,
                Body = task.Input,
            }
        );
        var source = Draft(store, next.Id, "Fictional source requiring a clearer example.");
        var other = Draft(store, next.Id, "Fictional unused alternative.");
        var revision = Apply<CompanyTask>(
            store,
            C("artifact.revise") with
            {
                Id = source.Id,
                ExpectedVersion = source.Version,
                Digest = source.Digest,
                Body = "Show the complete fictional fold.",
            }
        );

        Assert.Empty(Needs(store, next.Id));
        Assert.Equal("revision", Assert.Single(Needs(store, revision.Id)).Kind);
        var artifacts = MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts");
        Assert.Equal(canceledDraft, artifacts.Single(a => a.Id == canceledDraft.Id));
        Assert.Equal(other, artifacts.Single(a => a.Id == other.Id));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
