using HireZero.Core;

public sealed partial class CompanyMemoryTests
{
    private const string MeetingDecision =
        "Use patient cobalt campaign demonstrations. Follow-up requires separate approval.";

    private static CompanyMeeting ProposedMinutes(CompanyStore store)
    {
        var (_, meeting) = OrchestrationNoticeFixture.Seed(store);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.MemoryMeeting(meeting.Id)).Status);
        meeting = OrchestrationNoticeFixture.FinishDiscussion(store, meeting);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.MemoryMeeting(meeting.Id)).Status);
        return Apply<CompanyMeeting>(
            store,
            C("meeting.outcome") with
            {
                Id = meeting.Id,
                ExpectedVersion = meeting.Version,
                Body = MeetingDecision,
            }
        );
    }

    private static CompanyMeeting AcceptMinutes(CompanyStore store, CompanyMeeting? meeting = null)
    {
        meeting ??= ProposedMinutes(store);
        return Apply<CompanyMeeting>(
            store,
            C("meeting.close") with
            {
                Id = meeting.Id,
                ExpectedVersion = meeting.Version,
                Digest = meeting.OutcomeDigest,
            }
        );
    }

    private static CompanyMemoryInput MeetingInput(CompanyMeeting meeting) =>
        Input() with
        {
            MeetingId = meeting.Id,
            Quote = "patient cobalt campaign demonstrations",
        };

    [Fact]
    public void AcceptedMinutesNeedExactCitationAndSeparateMemoryApproval()
    {
        var store = Store();
        var meeting = ProposedMinutes(store);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.MemoryMeeting(meeting.Id)).Status);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => Save(store, MeetingInput(meeting))).Status);
        meeting = AcceptMinutes(store, meeting);
        var source = store.MemoryMeeting(meeting.Id);
        Assert.Equal(meeting.Version, source.Version);
        Assert.Equal(meeting.OutcomeDigest, source.Digest);
        Assert.Equal(MeetingDecision, source.Text);
        var input = MeetingInput(meeting);
        Assert.Equal(
            422,
            Assert.Throws<WorkspaceException>(() => Save(store, input with { Quote = "invented conclusion" })).Status
        );
        Assert.Equal(
            422,
            Assert.Throws<WorkspaceException>(() => Save(store, input with { ArtifactId = "fictional-result" })).Status
        );
        var draft = Save(store, input);
        Assert.Empty(store.RetrieveMemory("cobalt").Items);
        var approved = Approve(store, draft);
        var excerpt = Assert.Single(Store().RetrieveMemory("cobalt").Items);
        Assert.Equal(approved.Id, excerpt.MemoryId);
        Assert.Null(excerpt.Origin);
        Assert.Equal(
            new CompanyMemoryMeetingOrigin(meeting.Id, meeting.Version, meeting.OutcomeDigest!, input.Quote!),
            excerpt.MeetingOrigin
        );
        var other = Store("fictional-meeting-neighbor");
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.MemoryMeeting(meeting.Id)).Status);
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => Save(other, input)).Status);
        Assert.Empty(other.RetrieveMemory("cobalt").Items);
    }

    [Fact]
    public void MeetingDecisionReachesOnlyItsApprovedAudienceAndRevocationFencesPreparedWork()
    {
        var store = Store();
        var meeting = AcceptMinutes(store);
        var work = MeasuredQueueChecks.Seed(store);
        var other = MeasuredQueueChecks.Seed(store);
        var draft = Save(store, MeetingInput(meeting) with { EmployeeId = work.EmployeeId });
        Assert.Empty(store.RetrieveMemory("cobalt", work.EmployeeId).Items);
        var memory = Approve(store, draft);
        Assert.Empty(store.RetrieveMemory("cobalt", other.EmployeeId).Items);
        Assert.Empty(store.RetrieveMemory("cobalt", work.EmployeeId, true).Items);
        var attempt = Prepare(store, work);
        var excerpt = Assert.Single(store.TextCall(attempt.Id).Context.Memory!.Items);
        Assert.Equal(memory.MeetingOrigin, excerpt.MeetingOrigin);
        store.Apply(Review(memory, "revoke"));
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(attempt)));
        Assert.Empty(store.RetrieveMemory("cobalt", work.EmployeeId).Items);
    }

    [Fact]
    public void ChangedMinutesFenceDraftApprovalRetrievalAnalysisAndPreparedDispatch()
    {
        var store = Store();
        var meeting = AcceptMinutes(store);
        var memory = Approve(store, Save(store, MeetingInput(meeting)));
        var draft = Save(store, MeetingInput(meeting) with { Title = "Another cobalt decision" });
        var work = MeasuredQueueChecks.Seed(store);
        var attempt = Prepare(store, work);
        Assert.Equal(memory.Id, Assert.Single(store.TextCall(attempt.Id).Context.Memory!.Items).MemoryId);
        // Simulate a changed retained source: closed meetings have no public edit command.
        using (var db = new StoreConnection(store.Database.Open(), store.Database))
        {
            var changed = meeting with { Version = meeting.Version + 1 };
            db.Execute(
                "UPDATE co_meetings SET revision=$0,payload=$1 WHERE company_id=$2 AND id=$3",
                changed.Version,
                CompanyJson.Encode(changed),
                store.CompanyId,
                meeting.Id
            );
        }
        Assert.Throws<WorkspaceException>(() => Approve(store, draft));
        Assert.Empty(store.RetrieveMemory("cobalt").Items);
        Assert.False(store.Memory(memory.Id).Usable);
        Assert.Equal("Its cited meeting minutes are no longer accepted/current.", store.Memory(memory.Id).Exclusion);
        Assert.False(store.Memory(memory.Id).AnalysisEligible);
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(attempt)));
    }

    [Fact]
    public void DeletingUnapprovedMeetingMemoryErasesItsCitationFromReplayReceipts()
    {
        var store = Store();
        var meeting = AcceptMinutes(store);
        var command = C("memory.save") with { Body = CompanyJson.Encode(MeetingInput(meeting)) };
        var draft = Apply<CompanyMemoryItem>(store, command);
        Assert.NotNull(draft.MeetingOrigin);
        var deleted = Apply<CompanyMemoryItem>(store, Delete(draft));
        Assert.Null(deleted.MeetingOrigin);
        Assert.Null(deleted.Definition.MeetingId);
        Assert.Null(deleted.Definition.Quote);
        Assert.Equal(deleted, Apply<CompanyMemoryItem>(store, command));
        Assert.Equal(MeetingDecision, store.MemoryMeeting(meeting.Id).Text);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void MemoryWithoutMeetingCitationKeepsItsOriginalDigestAndWireShape()
    {
        var store = Store();
        var draft = Save(store);
        Assert.Equal(
            CompanyJson.Digest(
                new
                {
                    input = draft.Definition,
                    origin = draft.Origin,
                    replacement = draft.Replacement,
                }
            ),
            draft.Digest
        );
        Assert.DoesNotContain("meetingId", CompanyJson.Encode(draft.Definition));
        Assert.DoesNotContain("meetingOrigin", CompanyJson.Encode(draft));
        Approve(store, draft);
        Assert.DoesNotContain("meetingOrigin", CompanyJson.Encode(Assert.Single(store.RetrieveMemory("cobalt").Items)));
    }
}
