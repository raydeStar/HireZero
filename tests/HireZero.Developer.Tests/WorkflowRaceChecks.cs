using HireZero.Core;
using HireZero.Roles.Developer;

public static class WorkflowRaceChecks
{
    sealed class PausingGitHub : SyntheticGitHub
    {
        public bool Pause;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<string> CommandAsync(
            string executable,
            string? root,
            string[] args,
            string? input,
            CancellationToken token
        )
        {
            if (Pause && args.Contains("user"))
            {
                Pause = false;
                Entered.SetResult();
                await Release.Task;
            }
            return await base.CommandAsync(executable, root, args, input, token);
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var stage in new[] { "confirm", "scoping", "publish", "cancel" })
        {
            var root = Path.Combine(GitHubReviewClient.CheckoutRoot, "race-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var id = Guid.NewGuid().ToString("N");
            var checkout = Path.Combine(GitHubReviewClient.CheckoutRoot, id);
            try
            {
                var db = Path.Combine(root, "test.sqlite");
                var requests = new RequestStore(db);
                var store = new GitHubReviewStore(db);
                var github = new PausingGitHub();
                var sema = new SyntheticSema();
                var reviewer = new SyntheticRuleReviewer();
                var source = store.Add(new ReviewRepository("repo", 1, 10, "owner/example", root, "source"));
                var request = requests.CreateRequest(
                    WorkspaceOwner.LocalIdentity,
                    "Example",
                    "Fix",
                    "Fix PR 42",
                    "test"
                );
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
                    reviewer,
                    requests
                );
                if (stage == "confirm")
                {
                    github.Pause = true;
                    var pending = service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot);
                    await github.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    requests.AddMessage(
                        request.Id,
                        WorkspaceOwner.LocalIdentity,
                        "Stop, the requirements changed",
                        "stop"
                    );
                    github.Release.SetResult();
                    try
                    {
                        await pending;
                        check(false, "A changed message during confirmation cannot consume approval or start work.");
                    }
                    catch (WorkspaceException) { }
                    check(
                        sema.Tasks == 0 && sema.Builders == 0 && github.Writes == 0 && workflow.RepositoryId is null,
                        "Confirm TOCTOU is blocked even when a provider ignores cancellation."
                    );
                    // The stale proposal is rejected after restart too; there is no inherited consent lease.
                    try
                    {
                        await service.ConfirmAsync(store.Workflows(1).Single(), default, workflow.ApprovalSnapshot);
                        check(false, "A stale proposal cannot resume after restart.");
                    }
                    catch (WorkspaceException)
                    {
                        check(true, "A stale proposal cannot resume after restart.");
                    }
                    continue;
                }
                workflow = await service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot);
                if (stage == "cancel")
                {
                    await service.CancelAsync(workflow, default);
                    await service.AdvanceAsync(workflow, default);
                    check(
                        sema.Builders == 0 && reviewer.Calls == 0 && store.Workflows(1).Single().State == "canceled",
                        "A stale workflow object cannot continue after explicit cancellation."
                    );
                    continue;
                }
                if (stage == "publish")
                {
                    await service.AdvanceAsync(workflow, default);
                    await service.AdvanceAsync(store.Workflows(1).Single(), default);
                    workflow = store.Workflows(1).Single();
                    check(workflow.State == "publication-proposed", "The race fixture reaches publication approval.");
                }
                requests.AddMessage(request.Id, WorkspaceOwner.LocalIdentity, "Stop, the requirements changed", "stop");
                if (stage == "publish")
                {
                    try
                    {
                        await service.PublishAsync(workflow, workflow.PublicationSnapshot, default);
                        check(false, "Publication requires the current conversation revision.");
                    }
                    catch (WorkspaceException)
                    {
                        check(true, "Publication requires the current conversation revision.");
                    }
                    check(github.Writes == 0, "A changed conversation cannot reuse old publication authority.");
                }
                else
                {
                    await service.AdvanceAsync(workflow, default);
                    await service.AdvanceAsync(workflow, default);
                    check(
                        sema.Builders == 0
                            && reviewer.Calls == 0
                            && store.Workflows(1).Single().State == "scope-changed",
                        "A new human message stops queued coding and judgment; stale workflow objects cannot replay it."
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
