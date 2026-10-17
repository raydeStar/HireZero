using HireZero.Core;
using HireZero.Roles.Developer;

public static class HomeApprovalChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "home-approval-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var requests = new RequestStore(path);
            var store = new GitHubReviewStore(path);
            var owner = new WorkspaceOwner(WorkspaceOwner.LocalIdentity);
            requests.SetPerson("coworker@example.invalid", "trusted", false);
            var request = requests.CreateRequest(
                "coworker@example.invalid",
                "example",
                "Repair",
                "Fix PR 42",
                "home-approval"
            );
            requests.AllowAgent(request.Id);
            var workflow = new PullRequestWorkflow(
                "home-approval",
                request.Id,
                1,
                GitHubRepositoryChat.ProposalKey(request, []),
                "owner/example",
                10,
                42,
                new string('a', 40),
                "evidence",
                PullRequestWorkflowChecks.Strategy(),
                "fake"
            )
            {
                ConsentGeneration = requests.AgentConsentGeneration(request.Id),
            };
            store.SaveWorkflow(workflow);
            var pending = PendingWorkflowApprovals.For(store, requests, owner, 1);
            check(
                pending is [{ Allowed: true }]
                    && pending[0].Workflow.Id == workflow.Id
                    && pending[0].Workflow.ApprovalSnapshot == workflow.ApprovalSnapshot,
                "Home exposes the exact canonical workflow and snapshot."
            );
            check(
                PendingWorkflowApprovals.For(store, requests, owner, 2).Length == 0,
                "Other connected accounts cannot receive pending approvals."
            );
            store.ClaimApproval(pending[0].Workflow, "repair", pending[0].Workflow.ApprovalSnapshot);
            check(
                PendingWorkflowApprovals.For(new GitHubReviewStore(path), requests, owner, 1) is [{ Allowed: false }],
                "A consumed Home/detail approval remains disabled across restarts."
            );
            requests.SetPerson(request.Requester, "trusted", true);
            check(
                PendingWorkflowApprovals.For(store, requests, owner, 1) is [{ Allowed: false }],
                "Revoked request disables Home approval presentation."
            );
            requests.AddMessage(request.Id, owner.Login, "Changed scope", "scope-change");
            check(
                PendingWorkflowApprovals.For(store, requests, owner, 1).Length == 0,
                "A new conversation scope removes stale Home proposals."
            );
            store.SaveWorkflow(workflow with { State = "canceled" });
            check(
                PendingWorkflowApprovals.For(store, requests, owner, 1).Length == 0,
                "Denied proposals disappear from Home after restart/read."
            );
            var restart = new GitHubReviewStore(path);
            check(
                PendingWorkflowApprovals.For(restart, requests, owner, 1).Length == 0,
                "Restart never replays denied proposals."
            );
        }
        finally
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
