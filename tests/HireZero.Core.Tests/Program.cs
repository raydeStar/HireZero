try
{
var count = 0;
void Check(bool condition, string message) { count++; if (!condition) throw new SyntheticCheckFailure(message); }
await AgentShiftChecks.RunAsync(Check);
await ShiftProposalChecks.RunAsync(Check);
await ScopedCollectorChecks.RunAsync(Check);
await IntakeChecks.RunAsync(Check);
await GuardAndSessionChecks.RunAsync(Check);
await SteeringChecks.RunAsync(Check);
await BriefChecks.RunAsync(Check);
await MemoryChecks.RunAsync(Check);
await MemorySuggestionChecks.RunAsync(Check);
await CommandTextChecks.RunAsync(Check);
await WorkingContextChecks.RunAsync(Check);
await TailnetChecks.RunAsync(Check);
await PeopleChecks.RunAsync(Check);
await TriageChecks.RunAsync(Check);
await RequestApprovalChecks.RunAsync(Check);
ReplyRecoveryChecks.Run(Check);
await BacklogChecks.RunAsync(Check);
await OwnerTaskChecks.RunAsync(Check);
await WorkLogChecks.RunAsync(Check);
await AccessChecks.RunAsync(Check);
await ConversationListChecks.RunAsync(Check);
await WorkStateChecks.RunAsync(Check);
await ThreadTitleChecks.RunAsync(Check);
await HomeChecks.RunAsync(Check);
await OwnerTaskKindChecks.RunAsync(Check);
await BudgetChecks.RunAsync(Check);
await ActivityLogChecks.RunAsync(Check);
Console.WriteLine($"PASS {count} core queue, shift, schedule, role catalog, intake, content guard, session, steering, daily brief and memory checks");
}
catch (Exception error)
{
    Console.Error.WriteLine(error is SyntheticCheckFailure ? "FAIL " + error.Message : "FAIL " + error.GetType().Name + ": " + error.Message);
    foreach (var frame in new System.Diagnostics.StackTrace(error, true).GetFrames().Take(10))
        Console.Error.WriteLine(Path.GetFileName(frame.GetFileName()) + ":" + frame.GetFileLineNumber() + " " + frame.GetMethod()?.Name);
    Environment.ExitCode = 1;
}
sealed class SyntheticCheckFailure(string message) : Exception(message);
