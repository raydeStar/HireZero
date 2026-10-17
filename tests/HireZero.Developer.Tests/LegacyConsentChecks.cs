using HireZero.Core;
using HireZero.Roles.Developer;
using Microsoft.Data.Sqlite;

public static class LegacyConsentChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var requester in new[] { "coworker@example.invalid", WorkspaceOwner.LocalIdentity })
        {
            var root = Path.Combine(GitHubReviewClient.CheckoutRoot, "legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var id = Guid.NewGuid().ToString("N");
            var checkout = Path.Combine(GitHubReviewClient.CheckoutRoot, id);
            try
            {
                var db = Path.Combine(root, "test.sqlite");
                var requests = new RequestStore(db);
                var store = new GitHubReviewStore(db);
                var agents = new AgentStore(db);
                var owner = new WorkspaceOwner(WorkspaceOwner.LocalIdentity);
                requests.SetPerson(requester, "trusted", false);
                var request = requests.CreateRequest(requester, "Example", "Fix", "Fix PR 42", "legacy");
                requests.AllowAgent(request.Id);
                var github = new SyntheticGitHub();
                var sema = new SyntheticSema();
                var reviewer = new SyntheticRuleReviewer();
                store.Add(new ReviewRepository("repo", 1, 10, "owner/example", root, "source"));
                var evidence = await github.EvidenceAsync("owner/example", 42, false, default);
                var workflow = new PullRequestWorkflow(
                    id,
                    request.Id,
                    1,
                    GitHubRepositoryChat.ProposalKey(request, []),
                    evidence.Repository,
                    10,
                    42,
                    evidence.HeadSha,
                    evidence.Snapshot,
                    PullRequestWorkflowChecks.Strategy(),
                    "fake"
                )
                {
                    ConsentGeneration = requests.AgentConsentGeneration(request.Id),
                };
                store.SaveWorkflow(workflow);
                var service = new PullRequestWorkflowService(
                    store,
                    github,
                    new GitHubFixCoordinator(store, github, TestTools.Sema(store, sema)),
                    reviewer,
                    requests
                );
                workflow = await service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot);
                requests.SetPerson(requester, "trusted", true);
                requests.SetPerson(requester, "trusted", false);
                var queued = agents.EnqueueReply(request.Id, "Legacy reply", "legacy-reply");
                var active = agents.EnqueueProactive(
                    new("synthetic-generic", "legacy-generic", "Legacy generic work", "fixture")
                )!;
                agents.Transition(active.Id, "queued", "active");
                var finished = agents.EnqueueProactive(
                    new("synthetic-done", "legacy-done", "Completed receipt", "fixture")
                )!;
                agents.Transition(finished.Id, "queued", "done");
                requests.AddMessage(request.Id, requester, "History must survive upgrade", "history");
                // Recreate the actual pre-generation schema and serialized workflow shape.
                using (var connection = new SqliteConnection("Data Source=" + db + ";Pooling=False"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = """
                        ALTER TABLE agent_permissions DROP COLUMN consent_generation;
                        DROP TABLE request_agent_epochs; DROP TABLE request_authority_versions; DROP TABLE request_agent_upgrade_review;
                        UPDATE github_pr_workflows SET payload=json_remove(payload,'$.consentGeneration');
                        """;
                    command.ExecuteNonQuery();
                }
                requests = new RequestStore(db);
                store = new GitHubReviewStore(db);
                agents = new AgentStore(db);
                check(
                    !requests.AgentAllowed(request.Id)
                        && requests.AgentUpgradeReviewRequired(request.Id)
                        && !requests.CanUseAgent(request, owner)
                        && requests.AgentConsentGeneration(request.Id) > 0,
                    "Legacy authority is unknown and fails closed for coworker and owner requests."
                );
                check(
                    agents.Find(queued.Id)?.State == "canceled"
                        && agents.Find(active.Id)?.State == "canceled"
                        && agents.Find(finished.Id)?.State == "done",
                    "Upgrade cancels all legacy queued/active kinds without deleting completed receipts."
                );
                check(
                    requests.Messages(request.Id).Any(message => message.Body == "History must survive upgrade")
                        && store.Workflows(1).Single().State == "scoping",
                    "Upgrade preserves conversations and workflow history without continuing the consumed approval."
                );
                service = new PullRequestWorkflowService(
                    store,
                    github,
                    new GitHubFixCoordinator(store, github, TestTools.Sema(store, sema)),
                    reviewer,
                    requests
                );
                await service.AdvanceAsync(store.Workflows(1).Single(), default);
                await service.AdvanceAsync(store.Workflows(1).Single(), default);
                check(
                    sema.Builders == 0
                        && reviewer.Calls == 0
                        && github.Writes == 0
                        && store.Workflows(1).Single().State == "consent-revoked",
                    "A missing serialized workflow generation cannot match migrated execution authority."
                );
                requests.AllowAgent(request.Id);
                check(
                    requests.CanUseAgent(request, owner) && !requests.AgentUpgradeReviewRequired(request.Id),
                    "Fresh owner confirmation can enable new work after history review."
                );
                var currentGeneration = requests.AgentConsentGeneration(request.Id);
                var freshTask = agents.EnqueueReply(request.Id, "Fresh reviewed reply", "fresh");
                requests = new RequestStore(db);
                check(
                    requests.AgentConsentGeneration(request.Id) == currentGeneration
                        && agents.Find(freshTask.Id)?.State == "queued",
                    "Migration is idempotent; later restarts do not cancel newly confirmed work."
                );
                await service.AdvanceAsync(workflow, default);
                check(
                    sema.Builders == 0 && reviewer.Calls == 0 && github.Writes == 0,
                    "Fresh request permission still cannot revive the legacy consumed workflow approval."
                );
            }
            finally
            {
                if (Directory.Exists(checkout))
                    Directory.Delete(checkout, true);
                Directory.Delete(root, true);
            }
        }
    }
}
