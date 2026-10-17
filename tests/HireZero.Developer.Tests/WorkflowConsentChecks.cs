using HireZero.Core;
using HireZero.Roles.Developer;

public static class WorkflowConsentChecks
{
    sealed class Model : IModelGateway
    {
        public string DefaultModel => "fake";
        public string Reasoning => "none";

        public void Prepare(string cell) { }

        public void Stop(string cell) { }

        public Task<string> CompleteAsync(
            string cell,
            string instructions,
            string prompt,
            CancellationToken token,
            string? model = null,
            int maxTokens = 900
        ) => Task.FromResult("fake");
    }

    sealed class BlockingReview : IPullRequestReasoner
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;

        public Task<string> ReviewBusinessRulesAsync(FixStrategy strategy, string diff, CancellationToken token)
        {
            Calls++;
            Entered.SetResult();
            return Result.Task;
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (
            var scenario in new[]
            {
                "scoping",
                "publication",
                "during-review",
                "requester-revoked",
                "requester-rapid-restore",
                "requester-rapid-reallow",
            }
        )
        {
            var root = Path.Combine(GitHubReviewClient.CheckoutRoot, "consent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var id = Guid.NewGuid().ToString("N");
            var checkout = Path.Combine(GitHubReviewClient.CheckoutRoot, id);
            try
            {
                var db = Path.Combine(root, "test.sqlite");
                var requests = new RequestStore(db);
                var store = new GitHubReviewStore(db);
                var github = new SyntheticGitHub();
                var sema = new SyntheticSema();
                var reviewer = new SyntheticRuleReviewer();
                var blocking = new BlockingReview();
                var source = store.Add(new ReviewRepository("repo", 1, 10, "owner/example", root, "source"));
                var requester = scenario.StartsWith("requester-", StringComparison.Ordinal)
                    ? "coworker@example.invalid"
                    : WorkspaceOwner.LocalIdentity;
                if (scenario.StartsWith("requester-", StringComparison.Ordinal))
                    requests.SetPerson(requester, "trusted", false);
                var request = requests.CreateRequest(requester, "example", "Fix", "Fix PR 42", "test");
                if (scenario.StartsWith("requester-", StringComparison.Ordinal))
                    requests.AllowAgent(request.Id);
                var triage = new RequestTriageService(requests, new TriageStore(db), new Model());
                var evidence = await github.EvidenceAsync(source.Name, 42, false, default);
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
                );
                workflow = workflow with { ConsentGeneration = requests.AgentConsentGeneration(request.Id) };
                store.SaveWorkflow(workflow);
                var service = new PullRequestWorkflowService(
                    store,
                    github,
                    new GitHubFixCoordinator(store, github, sema),
                    scenario == "during-review" ? blocking : reviewer,
                    requests
                );
                workflow = await service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot);
                if (scenario is "publication" or "during-review")
                {
                    await service.AdvanceAsync(workflow, default);
                    workflow = store.Workflows(1).Single();
                    if (scenario == "during-review")
                    {
                        var running = service.AdvanceAsync(workflow, default);
                        await blocking.Entered.Task;
                        triage.Decide(request.Id, false, null);
                        blocking.Result.SetResult("{\"clear\":true,\"concerns\":[]}");
                        try
                        {
                            await running;
                        }
                        catch (WorkspaceException) { }
                        check(
                            store.Workflows(1).Single().State != "publication-proposed" && github.Writes == 0,
                            "Decline during a judgment await discards its result and cannot reach publication."
                        );
                        continue;
                    }
                    await service.AdvanceAsync(workflow, default);
                    workflow = store.Workflows(1).Single();
                    check(
                        workflow.State == "publication-proposed",
                        "The fixture reaches publication approval while consent is present."
                    );
                    triage.Decide(request.Id, false, null);
                    try
                    {
                        await service.PublishAsync(workflow, workflow.PublicationSnapshot, default);
                        check(false, "Declined work cannot publish.");
                    }
                    catch (WorkspaceException)
                    {
                        check(true, "Declined work cannot publish.");
                    }
                    check(github.Writes == 0, "Request consent is required in addition to publication approval.");
                    continue;
                }
                if (scenario.StartsWith("requester-", StringComparison.Ordinal))
                    requests.SetPerson(requester, "trusted", true);
                else
                    triage.Decide(request.Id, false, null);
                if (scenario is "requester-rapid-restore" or "requester-rapid-reallow")
                {
                    var generation = requests.AgentConsentGeneration(request.Id);
                    requests.SetPerson(requester, "trusted", false);
                    check(
                        generation > workflow.ConsentGeneration && !requests.AgentAllowed(request.Id),
                        "Restoring access cannot renew an earlier request grant."
                    );
                    if (scenario == "requester-rapid-reallow")
                        requests.AllowAgent(request.Id);
                    requests = new RequestStore(db);
                    store = new GitHubReviewStore(db);
                    service = new PullRequestWorkflowService(
                        store,
                        github,
                        new GitHubFixCoordinator(store, github, sema),
                        reviewer,
                        requests
                    );
                    check(
                        requests.AgentConsentGeneration(request.Id) == generation,
                        "Consent generation survives restart and restoration before observation."
                    );
                    if (scenario == "requester-rapid-reallow")
                        check(
                            requests.CanUseAgent(request, new(WorkspaceOwner.LocalIdentity)),
                            "A fresh request grant is distinct from the old workflow approval."
                        );
                }
                await service.AdvanceAsync(workflow, default);
                await service.AdvanceAsync(store.Workflows(1).Single(), default);
                check(
                    sema.Builders == 0
                        && reviewer.Calls == 0
                        && github.Writes == 0
                        && store.Workflows(1).Single().State == "consent-revoked",
                    "Decline/revocation after scoping must stop all workflow queues before builder or model launch."
                );
                if (scenario == "requester-rapid-reallow")
                {
                    var freshId = Guid.NewGuid().ToString("N");
                    var freshCheckout = Path.Combine(GitHubReviewClient.CheckoutRoot, freshId);
                    try
                    {
                        var fresh = new PullRequestWorkflow(
                            freshId,
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
                        store.SaveWorkflow(fresh);
                        fresh = await service.ConfirmAsync(fresh, default, fresh.ApprovalSnapshot);
                        await service.AdvanceAsync(fresh, default);
                        await service.AdvanceAsync(store.Workflows(1).Single(item => item.Id == freshId), default);
                        check(
                            sema.Builders == 1
                                && reviewer.Calls == 1
                                && github.Writes == 0
                                && store.Workflows(1).Single(item => item.Id == freshId).State
                                    == "publication-proposed",
                            "Restored access and a fresh request grant require a new proposal and approval before local work can resume."
                        );
                    }
                    finally
                    {
                        if (Directory.Exists(freshCheckout))
                            Directory.Delete(freshCheckout, true);
                    }
                }
                if (scenario == "requester-revoked")
                {
                    requests.SetPerson(requester, "trusted", false);
                    await service.AdvanceAsync(workflow, default);
                    check(
                        sema.Builders == 0,
                        "Restoring a person cannot replay the prior workflow's consumed approval."
                    );
                }
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
