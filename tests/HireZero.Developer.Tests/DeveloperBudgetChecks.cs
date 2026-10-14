using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;
using HireZero.Roles.Developer;
using Microsoft.Extensions.Logging.Abstractions;

// Budgets in the developer role: Codex's token report, a used-up budget refusing a coding start without consuming the approval,
// the finished run counted once, and a fix loop pausing for the owner instead of planning another round. No network or model.
public static class DeveloperBudgetChecks
{
    sealed class FakeRunner : ICodingRunner
    {
        public CodingRun? Next; public int Starts;
        public CodingRun Start(CodingRunRequest request) { Starts++; return Next! with { Root = request.Root }; }
        public CodingRun? Find(string id) => Next;
        public void Cancel(string id) { }
    }
    sealed class FailingCi : GitHubReviewClient
    {
        public string Head = new('b', 40);
        public override Task<PullRequestEvidence> EvidenceAsync(string repository, int number, bool logs, CancellationToken token) =>
            Task.FromResult(new PullRequestEvidence(repository, 10, number, "PR", Head, "owner", true, false, repository, 10, "fix", "main", "clean", true,
                [new PullRequestCheck("tests", "failure", null)], [], [], [], [], [], []));
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var stream = new CodexStream();
        stream.Read("""{"method":"thread/tokenUsage/updated","params":{"threadId":"t","turnId":"u","tokenUsage":{"total":{"totalTokens":4200,"inputTokens":4000},"last":{"totalTokens":200}}}}""");
        var usage = stream.Read("""{"method":"turn/completed","params":{"turn":{"status":"completed"}}}""").Events.Single(item => item.Type == BudgetLedger.CodingUsage);
        check(JsonSerializer.SerializeToElement(usage.Data).GetProperty("tokens").GetInt64() == 4200, "Codex's reported token total becomes the run's usage event.");
        check(new CodexStream().Read("""{"method":"turn/completed","params":{"turn":{"status":"completed"}}}""").Events.Count == 1, "A turn without a token report adds no usage event.");

        var root = Path.Combine(Path.GetTempPath(), "workspace-dev-budget-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var id = Guid.NewGuid().ToString("N"); var managed = Path.Combine(GitHubReviewClient.CheckoutRoot, id);
        try
        {
            var db = Path.Combine(root, "test.sqlite"); var requests = new RequestStore(db); var store = new GitHubReviewStore(db); var github = new SyntheticGitHub();
            var budgets = new BudgetLedger(db, requests, WorkspaceOwner.LocalIdentity); var runner = new FakeRunner();
            var service = new PullRequestWorkflowService(store, github, new GitHubFixCoordinator(store, github, new SyntheticSema()), new SyntheticRuleReviewer(), requests, runner, budgets: budgets);
            var request = requests.CreateRequest(WorkspaceOwner.LocalIdentity, "example", "Fix", "Fix PR 42", Guid.NewGuid().ToString());
            store.Add(new ReviewRepository("repo", 1, 10, "owner/example", root, "source"));
            var evidence = await github.EvidenceAsync("owner/example", 42, false, default);
            var workflow = new PullRequestWorkflow(id, request.Id, 1, GitHubRepositoryChat.ProposalKey(request, []), evidence.Repository, 10, 42, evidence.HeadSha, evidence.Snapshot,
                PullRequestWorkflowChecks.Strategy(), "openai/gpt-6.1-sol") { ConsentGeneration = requests.AgentConsentGeneration(request.Id) };
            store.SaveWorkflow(workflow);

            budgets.SetLimit("role:developer", new(Runs: 1));
            budgets.Record("coding:earlier", DeveloperBudget.Role, null, "coding", "sol", 0, 1, 600);
            string? refused = null;
            try { await service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot); } catch (ReviewAccessException error) { refused = error.Message; }
            check(refused?.StartsWith("Budget reached: the developer role's") == true && runner.Starts == 0 && store.Workflows(1).Single().State == "proposed",
                "A used-up budget refuses the coding start and leaves the approval unspent.");

            budgets.SetLimit("role:developer", new(Runs: 5));
            var start = DateTimeOffset.UtcNow;
            runner.Next = new("run-9", "running", "", PullRequestPolicy.BuilderModel, "high", "gpt-6.1-sol", null, null, [], start.ToString("O"), start.ToString("O"));
            workflow = await service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot);
            check(workflow.State == "coding" && runner.Starts == 1, "With room in the budget the same approval starts Sol.");
            runner.Next = runner.Next with { State = "failed", Error = "Codex exited before finishing.", UpdatedAt = start.AddMinutes(12).ToString("O"),
                Events = [new(BudgetLedger.CodingUsage, "Used.", "{\"tokens\":9000}", start.ToString("O"))] };
            await service.AdvanceAsync(workflow, default); await service.AdvanceAsync(store.Workflows(1).Single(), default);
            check(budgets.TaskTotal(request.Id) is { Runs: 1, Minutes: 12, Tokens: 9000 } && budgets.RoleMonth(DeveloperBudget.Role).Runs == 2,
                "The finished run is counted once against the developer and its task.");

            // A fix loop whose CI fails would plan round 2; with the budget used up it pauses for the owner instead.
            budgets.SetLimit("role:developer", new(Runs: 2));
            var ci = new FailingCi(); var now = DateTimeOffset.UtcNow.ToString("O");
            var loop = new FixLoop(Guid.NewGuid().ToString("N"), request.Id, 1, "owner/example", 42, "checking", 1, 6, false, null, "777", now, ci.Head, null, now, now);
            store.SaveLoop(loop);
            var before = requests.Messages(request.Id).Count();
            await new FixLoopDriver(store, ci, null!, null!, requests, NullLogger<FixLoopDriver>.Instance, budgets).AdvanceAsync(loop, default);
            loop = store.Loops(request.Id).Single();
            check(loop.State == "needs-you" && loop.Round == 1 && loop.Note!.Contains("budget reached") && loop.Note.Contains("CI fails on"),
                "At the limit the loop pauses as needs-you, saying the budget was reached and what round 2 would cover.");
            check(requests.Messages(request.Id).Count() == before && requests.OwnerMessages(request.Id).Any(m => m.Body.Contains("Fix loop paused at round 1: budget reached")),
                "No round is asked for or planned, and the owner is told why.");
        }
        finally { if (Directory.Exists(managed)) Directory.Delete(managed, true); Directory.Delete(root, true); }
    }
}
