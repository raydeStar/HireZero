using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;
using Microsoft.Extensions.Logging.Abstractions;

// The fix loop's decisions with a scripted GitHub: when to ask Codex, how it reads the verdict and CI, when it pauses, resumes
// and stops. No network or model; rounds that would plan with a model are exercised only up to the pause rules.
public static class FixLoopChecks
{
    sealed class LoopGitHub : GitHubReviewClient
    {
        public string Head = new('b', 40);
        public List<string> Posted = [];
        public string Reactions = "[]",
            Reviews = "[]",
            Comments = "[]",
            IssueComments = "[]";
        public string CheckState = "success";
        public bool PrOpen = true;

        protected override Task<string> CommandAsync(
            string executable,
            string? root,
            string[] args,
            string? input,
            CancellationToken token
        )
        {
            var endpoint = args.First(arg => arg.StartsWith("repos/", StringComparison.Ordinal));
            if (args.Contains("POST"))
            {
                Posted.Add(endpoint + " " + input);
                return Task.FromResult("{\"id\":777,\"created_at\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\"}");
            }
            if (endpoint.Contains("/commits/"))
                return Task.FromResult("{\"commit\":{\"committer\":{\"date\":\"2026-01-01T00:00:00Z\"}}}");
            if (endpoint.Contains("/issues/") && !endpoint.Contains("/reactions"))
                return Task.FromResult(IssueComments);
            return Task.FromResult(
                endpoint.Contains("/reactions") ? Reactions
                : endpoint.Contains("/reviews") ? Reviews
                : endpoint.Contains("/comments") ? Comments
                : throw new InvalidOperationException("Unexpected endpoint " + endpoint)
            );
        }

        public override Task<PullRequestEvidence> EvidenceAsync(
            string repository,
            int number,
            bool logs,
            CancellationToken token
        ) =>
            Task.FromResult(
                new PullRequestEvidence(
                    repository,
                    10,
                    number,
                    "PR",
                    Head,
                    "owner",
                    PrOpen,
                    false,
                    repository,
                    10,
                    "fix",
                    "main",
                    "clean",
                    true,
                    [new PullRequestCheck("tests", CheckState, null)],
                    [],
                    [],
                    [],
                    [],
                    [],
                    []
                )
            );
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-loop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "loop.sqlite");
            var store = new GitHubReviewStore(db);
            var requests = new RequestStore(db);
            var github = new LoopGitHub();
            var driver = new FixLoopDriver(store, github, null!, null!, requests, NullLogger<FixLoopDriver>.Instance);
            var request = requests.CreateRequest(
                "owner@example.com",
                "owner/example",
                "Fix #42",
                "Fix it.",
                Guid.NewGuid().ToString()
            );
            var strategy = new FixStrategy("Fix", ["Step"], ["test"], [], [], false, "high", false, false, "");
            PullRequestWorkflow Workflow(string state, string? commit = null, string[]? rules = null) =>
                new(
                    Guid.NewGuid().ToString("N"),
                    request.Id,
                    1,
                    "k",
                    "owner/example",
                    10,
                    42,
                    new string('a', 40),
                    "snap",
                    strategy with
                    {
                        BusinessRuleChanges = rules ?? [],
                    },
                    "m",
                    state,
                    CommitSha: commit
                );

            // A proposal from before consent was recorded (or an older message) offers a refresh, never a Start that would be refused.
            var other = requests.CreateRequest(
                "owner@example.com",
                "owner/example",
                "Fix #43",
                "Fix it.",
                Guid.NewGuid().ToString()
            );
            var oldPlan = Workflow("proposed") with { RequestId = other.Id };
            store.SaveWorkflow(oldPlan);
            var board = new DeveloperOwnerTasks(
                store,
                new PipelineWatchStore(Path.Combine(root, "watch.sqlite")),
                requests
            );
            check(
                board.Tasks().Single(task => task.Id == "workflow:" + oldPlan.Id).Start == "refresh:" + oldPlan.Id,
                "A stale proposal offers Refresh strategy instead of Start."
            );
            var generation = requests.AgentConsentGeneration(other.Id);
            if (generation > 0)
            {
                var currentPlan = oldPlan with
                {
                    ConsentGeneration = generation,
                    MessageKey = GitHubRepositoryChat.ProposalKey(other, requests.Messages(other.Id)),
                };
                store.SaveWorkflow(currentPlan);
                check(
                    board.Tasks().Single(task => task.Id == "workflow:" + oldPlan.Id).Start?.StartsWith("loop:")
                        == true,
                    "A current proposal can be started."
                );
            }
            store.SaveWorkflow(oldPlan with { State = "canceled" });

            var first = Workflow("published", github.Head);
            store.SaveWorkflow(first);
            var loop = await driver.StartAsync(first, default, first.ApprovalSnapshot);
            check(
                loop.State == "working"
                    && loop.Round == 1
                    && store.Workflows(1, request.Id).Single().Round == 1
                    && requests.OwnerMessages(request.Id).Any(m => m.Body.Contains("Fix loop started")),
                "Keep fixing on a delivered fix starts a loop at its round and says so in the thread."
            );
            check(
                (await driver.StartAsync(first, default, first.ApprovalSnapshot)).Id == loop.Id
                    && store.Loops(request.Id).Length == 1,
                "Starting again while it runs returns the same loop."
            );
            try
            {
                await driver.StartAsync(first, default, "stale-snapshot");
                check(false, "An active loop cannot bypass the exact approval snapshot.");
            }
            catch (ReviewAccessException)
            {
                check(true, "An active loop still checks the exact approval snapshot.");
            }
            var another = Workflow("proposed");
            try
            {
                await driver.StartAsync(another, default, another.ApprovalSnapshot);
                check(false, "An old active loop cannot impersonate approval of a new strategy.");
            }
            catch (ReviewAccessException)
            {
                check(
                    store.Loops(request.Id).Single().WorkflowId == first.Id,
                    "A different strategy is blocked without renewing authority or replacing its active loop."
                );
            }

            await driver.AdvanceAsync(loop, default);
            loop = store.Loops(request.Id)[0];
            check(
                loop.State == "reviewing"
                    && loop.ReviewedSha == github.Head
                    && loop.ReviewCommentId == "777"
                    && github.Posted.Single().Contains("@codex review"),
                "A delivered round asks Codex to review that exact commit."
            );

            await driver.AdvanceAsync(loop with { PolledAt = null }, default);
            loop = store.Loops(request.Id)[0];
            check(loop.State == "reviewing", "No answer yet means it keeps waiting.");

            github.Reviews = JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        id = 5,
                        submitted_at = DateTimeOffset.UtcNow.AddHours(-1).ToString("O"),
                        commit_id = github.Head,
                        user = new { login = "chatgpt-codex-connector[bot]" },
                    },
                }
            );
            await driver.AdvanceAsync(loop with { PolledAt = null }, default);
            loop = store.Loops(request.Id)[0];
            check(loop.State == "reviewing", "A Codex review from before the request doesn't count.");

            github.Reactions = JsonSerializer.Serialize(
                new[] { new { content = "+1", user = new { login = "chatgpt-codex-connector[bot]" } } }
            );
            github.CheckState = "in_progress";
            await driver.AdvanceAsync(loop with { PolledAt = null }, default);
            loop = store.Loops(request.Id)[0];
            check(loop.State == "checking", "Codex's thumbs-up moves on to CI.");
            await driver.AdvanceAsync(loop with { PolledAt = null }, default);
            loop = store.Loops(request.Id)[0];
            check(loop.State == "checking", "Running checks are waited for.");
            github.CheckState = "success";
            await driver.AdvanceAsync(loop with { PolledAt = null }, default);
            loop = store.Loops(request.Id)[0];
            check(
                loop.State == "healthy" && requests.OwnerMessages(request.Id).Any(m => m.Body.Contains("is healthy ✓")),
                "Clean review plus green CI ends the loop as healthy."
            );

            // A review already requested after this commit (by hand, or before a restart) is reused, not asked again.
            var asked = new LoopGitHub
            {
                IssueComments = JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            id = 555,
                            body = "@codex review",
                            created_at = DateTimeOffset.UtcNow.ToString("O"),
                            user = new { login = "mlee" },
                        },
                    }
                ),
            };
            var reuse = loop with
            {
                Id = "reuse",
                State = "working",
                WorkflowId = first.Id,
                ReviewCommentId = null,
                StopAfterRound = false,
            };
            store.SaveLoop(reuse);
            await new FixLoopDriver(
                store,
                asked,
                null!,
                null!,
                requests,
                NullLogger<FixLoopDriver>.Instance
            ).AdvanceAsync(reuse, default);
            check(
                store.Loops(request.Id).First(item => item.Id == "reuse")
                    is { State: "reviewing", ReviewCommentId: "555" }
                    && asked.Posted.Count == 0,
                "An existing review request after the commit is reused instead of posting another."
            );

            var hooray = new LoopGitHub
            {
                IssueComments = JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            id = 1,
                            body = "Codex Review: Didn't find any major issues. Hooray!\n\n**Reviewed commit:** `"
                                + new string('b', 10)
                                + "`",
                            created_at = DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"),
                            user = new { login = "chatgpt-codex-connector[bot]" },
                        },
                    }
                ),
            };
            check(
                (
                    await hooray.CodexVerdictAsync(
                        "owner/example",
                        42,
                        "777",
                        DateTimeOffset.UtcNow.ToString("O"),
                        new string('b', 40),
                        default
                    )
                ).State == "clean"
                    && (
                        await hooray.CodexVerdictAsync(
                            "owner/example",
                            42,
                            "777",
                            DateTimeOffset.UtcNow.ToString("O"),
                            new string('c', 40),
                            default
                        )
                    ).State == "pending",
                "Codex's \"didn't find any major issues\" comment on the head commit counts as clean; on another commit it doesn't."
            );
            // A strategy still waiting on the owner is closed out once its PR is merged or closed.
            var stale = Workflow("proposed");
            store.SaveWorkflow(stale);
            var sweep = new ClosedPullRequestSweep(store, github, NullLogger<ClosedPullRequestSweep>.Instance);
            check(
                await sweep.SweepAsync(default) == 0
                    && store.Workflows(1, request.Id).First(w => w.Id == stale.Id).State == "proposed",
                "An open PR keeps its waiting strategy."
            );
            github.PrOpen = false;
            check(
                await sweep.SweepAsync(default) >= 1
                    && store.Workflows(1, request.Id).First(w => w.Id == stale.Id).State
                        == ClosedPullRequestSweep.ClosedState
                    && store.Workflows(1, request.Id).First(w => w.Id == first.Id).State == "published",
                "A merged or closed PR closes out waiting strategies only."
            );
            github.PrOpen = true;
            // Findings at the round limit pause instead of starting another round.
            var verdictGitHub = new LoopGitHub
            {
                Reviews = JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            id = 9,
                            submitted_at = DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"),
                            commit_id = new string('b', 40),
                            user = new { login = "chatgpt-codex-connector[bot]" },
                        },
                    }
                ),
                Comments = JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            pull_request_review_id = 9,
                            path = "src/a.js",
                            line = 12,
                            body = "**<sub>![P1 Badge](x)</sub>  Validate the client ID**\n\nDetails.",
                        },
                    }
                ),
            };
            var verdict = await verdictGitHub.CodexVerdictAsync(
                "owner/example",
                42,
                "777",
                "2026-10-06T10:00:00Z",
                new string('b', 40),
                default
            );
            check(
                verdict is { State: "findings", Findings: ["src/a.js:12: P1 Validate the client ID"] },
                "Codex findings are read as path, line, priority and title."
            );
            var limited = loop with { Id = "limit", State = "reviewing", Round = 6, MaxRounds = 6, PolledAt = null };
            store.SaveLoop(limited);
            await new FixLoopDriver(
                store,
                verdictGitHub,
                null!,
                null!,
                requests,
                NullLogger<FixLoopDriver>.Instance
            ).AdvanceAsync(limited, default);
            check(
                store.Loops(request.Id).First(item => item.Id == "limit") is { State: "needs-you", Note: { } note }
                    && note.Contains("limit")
                    && note.Contains("Validate the client ID"),
                "At the round limit, new findings pause the loop with what's left."
            );

            // A round that pauses (missing proof, say) pauses the loop; once the owner gets it delivered, the loop carries on.
            var paused = Workflow("validation-attention") with
            {
                Error = "Missing command evidence for a regression.",
            };
            store.SaveWorkflow(paused);
            var waiting = loop with { Id = "pause", State = "working", WorkflowId = paused.Id, Round = 2 };
            store.SaveLoop(waiting);
            await driver.AdvanceAsync(waiting, default);
            waiting = store.Loops(request.Id).First(item => item.Id == "pause");
            check(
                waiting is { State: "needs-you", PausedOn: "validation-attention" }
                    && waiting.Note!.Contains("Missing command evidence"),
                "A paused round pauses the loop and says why."
            );
            store.SaveWorkflow(paused with { State = "published", CommitSha = github.Head });
            await driver.AdvanceAsync(waiting, default);
            waiting = store.Loops(request.Id).First(item => item.Id == "pause");
            check(
                waiting.State == "working",
                "When the owner clears the pause and the round is delivered, the loop picks back up."
            );

            // Stop after this round: the delivered round ends the loop instead of asking for another review.
            waiting = await driver.StopAsync(waiting, now: false, default);
            var posted = github.Posted.Count;
            await driver.AdvanceAsync(waiting, default);
            waiting = store.Loops(request.Id).First(item => item.Id == "pause");
            check(
                waiting.State == "stopped" && github.Posted.Count == posted,
                "Stop after this round lets it finish, then stops without asking for another review."
            );
            check(driver.Resume(waiting).State == "working", "A stopped loop can be resumed.");

            // A clean strategy is scoped by the loop but waits for the owner before any code changes.
            var clean = Workflow("proposed");
            store.SaveWorkflow(clean);
            var ready = loop with { Id = "ready", State = "planning", WorkflowId = clean.Id, Round = 4 };
            store.SaveLoop(ready);
            await driver.AdvanceAsync(ready, default);
            check(
                store.Loops(request.Id).First(item => item.Id == "ready")
                    is { State: "needs-you", PausedOn: "proposed", Note: { } readyNote }
                    && readyNote.Contains("strategy is ready")
                    && store.Workflows(1, request.Id).First(w => w.Id == clean.Id).State == "proposed",
                "A behavior fix waits for the owner's approval; the loop never starts it."
            );
            store.SaveWorkflow(clean with { State = "coding" });
            await driver.AdvanceAsync(store.Loops(request.Id).First(item => item.Id == "ready"), default);
            check(
                store.Loops(request.Id).First(item => item.Id == "ready").State == "working",
                "Once the owner approves, the loop carries on."
            );
            var audit = Workflow("proposed") with
            {
                Strategy = strategy with { Tests = ["npm audit --omit=dev --audit-level=high"] },
            };
            check(
                FixLoopDriver.NoBrainer(audit)
                    && !FixLoopDriver.NoBrainer(clean)
                    && !FixLoopDriver.NoBrainer(
                        audit with
                        {
                            Strategy = audit.Strategy with { Questions = ["Major bump?"] },
                        }
                    ),
                "Only dependency-only remediation with no questions or rule changes counts as a no-brainer."
            );
            // A strategy with a business-rule change is never approved by the loop.
            var ruled = Workflow("proposed", rules: ["Readiness changes."]);
            store.SaveWorkflow(ruled);
            var planning = loop with { Id = "rules", State = "planning", WorkflowId = ruled.Id, Round = 3 };
            store.SaveLoop(planning);
            await driver.AdvanceAsync(planning, default);
            check(
                store.Loops(request.Id).First(item => item.Id == "rules")
                    is { State: "needs-you", PausedOn: "proposed" }
                    && store.Workflows(1, request.Id).First(w => w.Id == ruled.Id).State == "proposed",
                "A business-rule change pauses the loop for the owner instead of being approved automatically."
            );
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
