// Each synthetic check group runs as one xUnit test; tests in one class run sequentially.
public sealed class CoreSuite
{
    [Fact]
    public Task AgentShift() => AgentShiftChecks.RunAsync(Verify.Check);

    [Fact]
    public Task ShiftProposal() => ShiftProposalChecks.RunAsync(Verify.Check);

    [Fact]
    public Task ScopedCollector() => ScopedCollectorChecks.RunAsync(Verify.Check);

    [Fact]
    public Task Intake() => IntakeChecks.RunAsync(Verify.Check);

    [Fact]
    public Task GuardAndSession() => GuardAndSessionChecks.RunAsync(Verify.Check);

    [Fact]
    public Task Brief() => BriefChecks.RunAsync(Verify.Check);

    [Fact]
    public Task Memory() => MemoryChecks.RunAsync(Verify.Check);

    [Fact]
    public Task MemorySuggestion() => MemorySuggestionChecks.RunAsync(Verify.Check);

    [Fact]
    public Task CommandText() => CommandTextChecks.RunAsync(Verify.Check);

    [Fact]
    public Task WorkingContext() => WorkingContextChecks.RunAsync(Verify.Check);

    [Fact]
    public Task Tailnet() => TailnetChecks.RunAsync(Verify.Check);

    [Fact]
    public Task People() => PeopleChecks.RunAsync(Verify.Check);

    [Fact]
    public Task Triage() => TriageChecks.RunAsync(Verify.Check);

    [Fact]
    public Task RequestApproval() => RequestApprovalChecks.RunAsync(Verify.Check);

    [Fact]
    public void ReplyRecovery() => ReplyRecoveryChecks.Run(Verify.Check);

    [Fact]
    public Task Backlog() => BacklogChecks.RunAsync(Verify.Check);

    [Fact]
    public Task OwnerTask() => OwnerTaskChecks.RunAsync(Verify.Check);

    [Fact]
    public Task WorkLog() => WorkLogChecks.RunAsync(Verify.Check);

    [Fact]
    public Task Access() => AccessChecks.RunAsync(Verify.Check);

    [Fact]
    public Task ConversationList() => ConversationListChecks.RunAsync(Verify.Check);

    [Fact]
    public Task WorkState() => WorkStateChecks.RunAsync(Verify.Check);

    [Fact]
    public Task ThreadTitle() => ThreadTitleChecks.RunAsync(Verify.Check);

    [Fact]
    public Task Home() => HomeChecks.RunAsync(Verify.Check);

    [Fact]
    public Task OwnerTaskKind() => OwnerTaskKindChecks.RunAsync(Verify.Check);

    [Fact]
    public Task Budget() => BudgetChecks.RunAsync(Verify.Check);

    [Fact]
    public Task ActivityLog() => ActivityLogChecks.RunAsync(Verify.Check);
}

sealed class SyntheticCheckFailure(string message) : Exception(message);
