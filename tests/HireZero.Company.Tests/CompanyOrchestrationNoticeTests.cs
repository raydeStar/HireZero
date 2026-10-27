using HireZero.Core;
using Microsoft.Data.Sqlite;
using static OrchestrationNoticeFixture;

public sealed class CompanyOrchestrationNoticeTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("orchestration-notices-");

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions(), companyId: company);

    [Fact]
    public void PlanApprovalAndCompletionHaveDurableExactTargetsWithoutDuplicateReplay()
    {
        var store = Store();
        var seed = Seed(store);
        var proposed = Assert.Single(store.Inbox(Owner).Items).Notice;
        Assert.Equal(("plan", seed.Plan.Id, "attention"), (proposed.Destination, proposed.TargetId, proposed.Category));
        Assert.Empty(seed.Plan.TaskIds);
        Assert.Contains(seed.Plan.Objective, proposed.Detail);
        var completed = CompletePlan(store, seed.Plan);
        Assert.Equal("completed", completed.State);
        var notices = store.Inbox(Owner).Items.Where(i => i.Notice.Destination == "plan").ToArray();
        Assert.Equal(2, notices.Length);
        Assert.Equal("completed", notices[0].Notice.Category);
        Assert.Equal(seed.Plan.Id, notices[0].Notice.TargetId);
        var task = store.Work(Assert.Single(completed.TaskIds));
        var count = store.UnreadNotices(Owner);
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = task.Version })
        );
        Assert.Equal(count, Store().UnreadNotices(Owner));
        Assert.Empty(Store("co-notice-neighbor").Inbox(Owner).Items);
        Assert.Equal(
            404,
            Assert
                .Throws<WorkspaceException>(() =>
                    Store("co-notice-neighbor")
                        .Apply(C("notification.mark") with { Id = proposed.Id, Decision = "read" })
                )
                .Status
        );
        store.Apply(C("member.invite") with { Body = "viewer@example.invalid", Decision = "viewer" });
        var viewer = store.SignIn("fictional-viewer", "viewer@example.invalid", true, "Fictional Viewer")!;
        store.Apply(C("notification.mark") with { Id = proposed.Id, Decision = "read" }, viewer.Subject, out _);
        Assert.False(store.Inbox(Owner).Items.Single(i => i.Notice.Id == proposed.Id).Read);
        Assert.True(Store().Inbox(viewer.Subject).Items.Single(i => i.Notice.Id == proposed.Id).Read);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void MeetingTurnsCollectIntoOneMinutesAlertAndAcceptanceNeverDispatchesFollowUp()
    {
        var store = Store();
        var meeting = FinishDiscussion(store, Seed(store).Meeting);
        Assert.Equal("outcome_proposed", meeting.State);
        var discussion = Assert.Single(store.Inbox(Owner).Items, i => i.Notice.Destination == "meeting").Notice;
        Assert.Equal((meeting.Id, "attention"), (discussion.TargetId, discussion.Category));
        Assert.Contains(meeting.Title, discussion.Detail);
        Assert.DoesNotContain(store.Inbox(Owner).Items, i => i.Notice.Destination == "task");
        var now = DateTimeOffset.UtcNow;
        var needs = store.Today("today", now.AddDays(-1), now.AddDays(1)).NeedsYou;
        Assert.DoesNotContain(needs, n => n.Kind == "review");
        Assert.Equal(meeting.Id, Assert.Single(needs, n => n.Kind == "meeting").TargetId);
        var count = Items<CompanyTask>(store, "tasks").Length;
        var propose = C("meeting.outcome") with
        {
            Id = meeting.Id,
            ExpectedVersion = meeting.Version,
            Body = "Fictional decision: wait. Follow-up still requires its own approval.",
        };
        meeting = A<CompanyMeeting>(store, propose);
        store.Apply(propose);
        Assert.Equal(2, store.Inbox(Owner).Items.Count(i => i.Notice.Destination == "meeting"));
        var close = C("meeting.close") with
        {
            Id = meeting.Id,
            ExpectedVersion = meeting.Version,
            Digest = meeting.OutcomeDigest,
        };
        store.Apply(close);
        store.Apply(close);
        var notices = Store().Inbox(Owner).Items.Where(i => i.Notice.Destination == "meeting").ToArray();
        Assert.Equal(3, notices.Length);
        Assert.Equal("completed", notices[0].Notice.Category);
        Assert.Equal(count, Items<CompanyTask>(store, "tasks").Length);
        Assert.DoesNotContain(
            store.Today("today", now.AddDays(-1), now.AddDays(1)).NeedsYou,
            n => n.Kind == "meeting" || n.Kind == "review"
        );
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void UncertainMeetingTurnAlertsTheExactWorkAndRecoveryDoesNotReplayIt()
    {
        var store = Store();
        var meeting = Seed(store).Meeting;
        meeting = A<CompanyMeeting>(
            store,
            C("meeting.start") with
            {
                Id = meeting.Id,
                ExpectedVersion = meeting.Version,
            }
        );
        var task = store.Work(meeting.ActiveTask!);
        var attempt = store.Claim(task.EmployeeId, true, task.Id)!;
        store.Capture(attempt.Id);
        Assert.True(store.AuthorizeDispatch(attempt.Id));
        store.FailAttempt(attempt.Id, "fictional-lost-reply", true);
        var notice = Assert.Single(store.Inbox(Owner).Items, i => i.Notice.Destination == "task").Notice;
        Assert.Equal(
            (task.Id, "attention", "Meeting work needs attention"),
            (notice.TargetId, notice.Category, notice.Title)
        );
        Assert.Equal("unknown", store.Work(task.Id).State);
        var reopened = Store();
        reopened.Recover();
        Assert.Null(reopened.Claim(task.EmployeeId, true, task.Id));
        Assert.Single(reopened.Inbox(Owner).Items, i => i.Notice.Destination == "task");
        Assert.Throws<WorkspaceException>(() =>
            reopened.Apply(
                C("meeting.next") with
                {
                    Id = meeting.Id,
                    ExpectedVersion = Items<CompanyMeeting>(reopened, "meetings").Single().Version,
                }
            )
        );
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
