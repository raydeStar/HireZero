namespace HireZero.Core;

// A recent reply or an open conversation is never evidence of a live execution.
public sealed record ConversationWorkState(string State, string Reason, string At, string Freshness);
public static class ConversationWorkStates
{
    public static ConversationWorkState For(ConversationSummary conversation, AgentTask? task, OwnerTask? workflow, bool shiftOn, bool needsGoAhead = false, bool superseded = false)
    {
        ConversationWorkState State(string state, string reason, string? at = null, string freshness = "recorded") =>
            new(state, reason, at ?? conversation.LastAt, freshness);
        if (conversation.Status == "archived") return State("Completed", "Archived conversation; history retained.");
        // A new queued/running turn takes precedence over an older workflow receipt.
        var newerTask = task is not null && (workflow is null || DateTimeOffset.TryParse(task.UpdatedAt, out var taskAt) &&
            DateTimeOffset.TryParse(workflow.UpdatedAt, out var workflowAt) && taskAt >= workflowAt);
        if (newerTask && task is { State: "active" }) return AgentActivity.IsRunning(task.Id)
            ? State("Running", "The current host holds this task's execution lease.", task.UpdatedAt, "live")
            : State("Paused", "Recorded active task has no current host lease; execution freshness is unknown.", task.UpdatedAt, "unknown");
        if (newerTask && task is { State: "queued" }) return task.Source == "proactive" && !shiftOn
            ? State("Paused", "Proactive work waits for an active shift.", task.UpdatedAt)
            : State("Queued", "A durable task is waiting for the worker.", task.UpdatedAt);
        if (workflow is not null)
        {
            if (workflow.Status == "pr-closed") return State("Completed", "The PR was merged or closed on GitHub.", workflow.UpdatedAt);
            if (superseded) return State("Superseded", "A newer conversation covers the same pull request.", workflow.UpdatedAt);
            if (workflow.Status is "proposed" or "publication-proposed")
                return State("Waiting for you", "A fix strategy is ready for your approval.", workflow.UpdatedAt);
            if (workflow.Status is "needs-input" or "needs-evidence" or "needs-you")
                return State("Waiting for you", "The fix has a question for you.", workflow.UpdatedAt);
            if (workflow.Status.EndsWith("-attention", StringComparison.Ordinal))
                return State("Waiting for you", "The fix paused for your decision" + (workflow.Blockers.FirstOrDefault() is { } why ? ": " + OwnerTaskText.Brief(why, 160) : "."), workflow.UpdatedAt);
            if (workflow.Approval == "needs-owner")
                return State("Waiting for you", "Review the pending decision in this conversation.", workflow.UpdatedAt);
            if (workflow.Status is "canceled" or "cancelled") return State("Cancelled", "The workflow was cancelled.", workflow.UpdatedAt);
            if (workflow.Status is "failed") return State("Failed", "The workflow failed; review its evidence.", workflow.UpdatedAt);
            if (workflow.Status is "published" or "merged" or "done" or "completed" or "healthy") return State("Completed", "The latest workflow has a terminal receipt.", workflow.UpdatedAt);
            if (workflow.Blockers.Length > 0 || workflow.Status.EndsWith("-attention", StringComparison.Ordinal) || workflow.Status is "consent-revoked" or "scope-changed") return State("Blocked", "The recorded workflow has a blocker.", workflow.UpdatedAt);
            if (workflow.ExecutionState == "live") return State("Running", "The current host holds this workflow's execution lease.", workflow.UpdatedAt, "live");
            if (workflow.ExecutionState == "queued") return State("Queued", "The coding run is queued in the current worker.", workflow.UpdatedAt);
            if (workflow.Status is "coding" or "validating" or "preparing" or "scoping" or "working")
                return State("Paused", "Workflow records alone do not prove the worker is still executing. Open its progress.", workflow.UpdatedAt, "unknown");
        }
        if (task is { State: "failed" }) return State("Failed", "The latest task failed; review its error.", task.UpdatedAt);
        if (task is { State: "canceled" }) return State("Cancelled", "The latest task was cancelled.", task.UpdatedAt);
        if (task is { State: "done" } && task.Kind != ConversationReplyHandler.ReplyKind) return State("Completed", "The latest agent task completed.", task.UpdatedAt);
        if (needsGoAhead) return State("Waiting for you", "Your message is waiting for your go-ahead before the agent works on it.");
        if (conversation.LastFromAgent) return conversation.AgentAsked
            ? State("Waiting for you", "The agent asked you something.")
            : State("Answered", "The agent answered; nothing is waiting.");
        return State("Paused", "No queued task or current execution lease is recorded.", freshness: "unknown");
    }
}
