namespace HireZero.Roles.Developer;

// Projection reads durable local records only. Opening the board never initiates connector reads or model calls.
public sealed class DeveloperOwnerTasks(GitHubReviewStore reviews, PipelineWatchStore watches, RequestStore requests, ICodingRunner? coding = null) : IOwnerTaskContributor
{
    // A proposal can start only while it matches the conversation's current consent and latest message; an older one
    // (including any written before consent was recorded) needs a fresh strategy first.
    bool Current(PullRequestWorkflow item) => item.ConsentGeneration > 0 && item.ConsentGeneration == requests.AgentConsentGeneration(item.RequestId) &&
        requests.FindRequest(item.RequestId) is { } request && GitHubRepositoryChat.ProposalKey(request, requests.Messages(request.Id)) == item.MessageKey;

    bool Ready(PullRequestWorkflow item) => Current(item) && reviews.ApprovalAvailable(item);

    public OwnerTask[] Tasks() => [.. reviews.Workflows().DistinctBy(item => item.RequestId).Select(item => new OwnerTask(
        "workflow:" + item.Id, item.Repository + " #" + item.Number, "GitHub workflow", "https://github.com/" + item.Repository + "/pull/" + item.Number,
        item.RequestId, item.State, reviews.WorkflowUpdatedAt(item.Id), "owner", "normal",
        item.Error is { Length: > 0 } error ? [error] : item.State is "proposed" or "publication-proposed" ? ["Owner approval required."] : [],
        item.State == "publication-proposed" ? "Review the exact diff and approve publication." : item.State == "proposed" && !Current(item) ? "This strategy is out of date. Refresh it to get a current one you can start."
            : item.State == "proposed" && !reviews.ApprovalAvailable(item) ? "This approval expired or was already used. Refresh the strategy before starting it."
            : item.State == "proposed" && item.Strategy.Questions.Length > 0 ? "Answer " + (item.Strategy.Questions.Length == 1 ? "1 question" : item.Strategy.Questions.Length + " questions") + " on the strategy. Nothing runs until you do."
            : item.State == "proposed" && item.Strategy.BusinessRuleChanges.Length > 0 && !item.RulesApproved ? "Approve the business-rule changes on the strategy. Nothing runs until you do."
            : item.State == "proposed" ? "Approve the strategy to start fixing." : "Open the workflow evidence.",
        item.State is "proposed" or "publication-proposed" ? "needs-owner" : "separate-actions-required",
        Kind: (item.Round is { } round ? "Round " + round + " · " : "") + KindOf(item), Detail: OwnerTaskText.Brief(item.Strategy.Summary),
        // Ready to run only when nothing needs a decision first; otherwise the card says Review.
        Start: item.State == "proposed" && !Ready(item) ? "refresh:" + item.Id
            : item.State == "proposed" && item.Strategy.Questions.Length == 0 && (item.Strategy.BusinessRuleChanges.Length == 0 || item.RulesApproved) ? "loop:" + item.Id + ":" + item.ApprovalSnapshot : null,
        ExecutionState: AgentActivity.IsRunning("workflow:" + item.Id) || item.State == "coding" && item.CodingRunId is { } runId && AgentActivity.IsRunning("coding:" + runId) ? "live"
            : item.State == "coding" && item.CodingRunId is { } queuedId && coding?.Find(queuedId)?.State == "queued" ? "queued" : null)),
        .. watches.All().Select(item => new OwnerTask("watch:" + item.Id, item.Scope.Repository + " run " + item.Scope.RunId,
            "GitHub pipeline watch", item.Url, null, item.State + (item.Conclusion is null ? "" : ": " + item.Conclusion), item.UpdatedAt.ToString("O"),
            "owner", "normal", item.Note is { } note ? [note] : [],
            item.State == "completed" ? "Review terminal evidence; approve diagnosis before invoking a model." : "Observe this exact run attempt.", "observation-only", Kind: "Pipeline watch", Detail: item.Note))];

    // What the fix is, from its strategy: dependency audit, failing checks, review findings, or a general PR fix.
    static string KindOf(PullRequestWorkflow item)
    {
        var text = item.Strategy.Trigger + " " + item.Strategy.Summary;
        bool Says(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
        return DirectCoding.AuditFix(item) || Says(@"npm audit|vulnerab|advisor") ? "Dependency fix"
            : Says(@"\b(build|ci|check|job|lint|typecheck|test)s?\b.{0,40}\b(fail|error|red|exit)") || Says(@"\b(fail|failing|broken)\b.{0,30}\b(build|ci|check|job|test)") ? "Build fix"
            : Says(@"\bcodex\b|review (thread|comment|finding)|\bP[0-3]\b") ? "Review fix" : "PR fix";
    }
}
