namespace HireZero.Roles.Developer;

public sealed record PendingWorkflowApproval(PullRequestWorkflow Workflow, bool Allowed, string? Blocker);

// Two presentations of the existing workflow, never another approval record or execution path.
public static class PendingWorkflowApprovals
{
    public static PendingWorkflowApproval[] For(
        GitHubReviewStore store,
        RequestStore requests,
        WorkspaceOwner owner,
        long accountId
    ) =>
        [
            .. store
                .Workflows(accountId)
                .DistinctBy(item => item.RequestId)
                .Where(item =>
                    item.State is "proposed" or "needs-evidence" or "publication-proposed" or "handoff-attention"
                )
                .Where(item =>
                    requests.FindRequest(item.RequestId) is { Status: not "archived" } request
                    && item.MessageKey == GitHubRepositoryChat.ProposalKey(request, requests.Messages(request.Id))
                )
                .Select(item =>
                {
                    var allowed =
                        requests.FindRequest(item.RequestId) is { } request
                        && requests.CanUseAgent(request, owner)
                        && item.ConsentGeneration > 0
                        && item.ConsentGeneration == requests.AgentConsentGeneration(item.RequestId)
                        && store.ApprovalAvailable(item);
                    return new PendingWorkflowApproval(
                        item,
                        allowed,
                        allowed
                            ? null
                            : "This approval needs refreshing or request access needs a fresh owner decision. Open the conversation to review it."
                    );
                })
                .Take(30),
        ];
}
