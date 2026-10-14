namespace HireZero.Core;

// Proposes tasks from already-collected inbox metadata, without another provider read or message-body access.
public sealed class InboxShiftSensor(IntakeStore intake, ShiftProposalStore proposals, AgentStore agents, WorkspaceOwner owner) : IShiftSensor
{
    public static OwnerTask[] Discover(IntakeRecord[] records, WorkspaceOwner owner, DateTimeOffset now) => records
        .Where(record => record.RequestId is null && record.Verdict is "review" or "act" && !record.Dismissed &&
            record.DecidedBy != "guard" && (record.Flags?.Length ?? 0) == 0 && DateTimeOffset.TryParse(record.CreatedAt, out var at) &&
            at <= now && now - at <= TimeSpan.FromDays(14))
        .DistinctBy(record => record.Id).OrderBy(record => record.Priority == "high" ? 0 : 1)
        .ThenByDescending(record => DateTimeOffset.Parse(record.CreatedAt))
        .Select(record => (record, subject: ContentGuard.Clean(record.Subject, 180), source: ContentGuard.Clean(record.Source, 80)))
        .Where(item => item.subject.Flags.Length == 0 && item.subject.Text.Length > 0 && item.source.Flags.Length == 0).Take(12)
        .Select(item => new OwnerTask("inbox-proposal:" + item.record.Id, "Review task candidate: " + item.subject.Text,
            "Recorded inbox metadata: " + item.source.Text, null, null, "proposal", item.record.CreatedAt, owner.Login,
            item.record.Priority == "high" ? "high" : "normal", ["Task ownership and agent permission have not been confirmed."],
            "Review the existing Inbox item, confirm whether it is your task, and choose a next step.", "needs-owner")).ToArray();

    public Task<(ProactiveWork[] Work, string Note)> SenseAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var shift = agents.Shift();
        if (!shift.On) return Task.FromResult<(ProactiveWork[], string)>(([], "Inbox discovery is off shift."));
        // Linked requests remain on the existing board; unlinked metadata grants no agent permission.
        var saved = agents.WithCurrentShift(shift, () =>
        {
            token.ThrowIfCancellationRequested(); proposals.Replace("inbox", Discover(intake.Recent(200), owner, DateTimeOffset.UtcNow));
        });
        return Task.FromResult<(ProactiveWork[], string)>(([], saved ?
            "Checked up to 200 existing Inbox metadata records; up to 12 task candidates are on Working On. No message bodies or new provider history were collected." :
            "Shift changed; inbox proposals were discarded."));
    }
}
